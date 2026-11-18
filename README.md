# UserService

The **GraphQL API for the user profile** of RSO (a WordPress-as-a-service platform): name, company and the **tenant slug** that
becomes part of every site address (`site.tenant.<domain>`).

UserService does **not** handle sign-up, log-in or passwords. Those belong to **Keycloak**. UserService only trusts the access
token Keycloak issues, and creates a user's profile the first time that user's token arrives.

```
browser --sign in/up--> Keycloak                       token (JWT)
browser --GraphQL + bearer token--> UserService --> PostgreSQL (UserServiceDb)
                                        |--UserCreated, TenantAssigned--> NATS --> PaymentService, ProvisioningService
```

How it fits with the other services, and how to run all of it on your machine or on Azure: the **Deployment** repository.

## What it does

- **GraphQL at `/graphql`** (needs `Authorization: Bearer <access token>`):

  ```graphql
  query { me { id email name lastname company tenant } }

  mutation { patchMe(input: { name: "Jane", lastname: "Doe", company: "Acme Corp" }) { tenant } }
  ```

  A field you leave out of `patchMe` is not changed; `null` clears it.
- **First request of a user:** `me` (or `patchMe`) creates the profile from the token (id = the token's `sub`, email, and first
  and last name) and publishes **`UserCreated`** to NATS in the same database transaction (Wolverine outbox). PaymentService uses
  it to create the Stripe customer.
- **Tenant slug:** when name, last name and company are all set, the user gets a unique slug (`Jane`, `Doe`, `Acme Corp` becomes
  `jane-doe-acme-corp`; `-2`, `-3`... if taken). It never changes afterwards. It is published as **`TenantAssigned`**.
- **One user per name in a company:** the same name and last name (ignoring case) twice in one company is refused with the
  GraphQL error code `DUPLICATE_NAME_IN_COMPANY`.
- **`GET /internal/users/{id}/tenant`**: the tenant of a user (404 if none). Cluster-internal, for ProvisioningService; the
  gateway does not route it.
- **Health and metrics:** `/health/live`, `/health/ready`, `/metrics` (Prometheus), `/swagger`.

## Run it locally

You need the .NET 10 SDK and Docker. The infrastructure (PostgreSQL, NATS, Keycloak) comes from the compose file in the
**Deployment** repository, which must sit next to this one:

```powershell
docker compose -f ..\Deployment\local\docker-compose.yml up -d     # PostgreSQL :5432, NATS :4222, Keycloak :8180
dotnet run --project Api                                             # http://localhost:5076 (GraphQL: /graphql)
```

In Development the database migrations run on start. To call the GraphQL API you need a Keycloak token: sign in through the web
app (WordpressAsAServiceUi, `npm start`) and copy the access token from the browser, or use the web app itself.

## Configuration

Settings are read from `appsettings*.json` and environment variables (use `__` for nesting, e.g. `Auth__Issuer`).

| Setting | Meaning |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL, database `UserServiceDb` |
| `Nats__Url` | NATS server, e.g. `nats://localhost:4222` |
| `Auth__Issuer` | the realm URL as the browser sees it (the token's `iss`), e.g. `http://localhost:8180/realms/rso` |
| `Auth__Audience` | `rso-api` (Keycloak puts it in the token) |
| `Auth__MetadataAddress` | Keycloak's discovery document, where the signing keys are read from |
| `Database__MigrateOnStartup` | `true` to run migrations on start (always on in Development) |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | optional: also send the logs to Application Insights |

## Tests

```powershell
dotnet test Test/Test.csproj
```

The tests use an in-memory database, so they do not need Docker or Keycloak. They cover creating the profile on first request,
publishing `UserCreated` only once, the tenant slug and the duplicate-name rule.

## Container image and release

`Api/Dockerfile` builds the image (build from this folder: `docker build -f Api/Dockerfile .`; it listens on port 8080).
**Push a git tag like `v1.2.3`** and the *Publish image* workflow builds and pushes `armodilo/userservice:v1.2.3` to Docker
Hub (it needs the repository secrets `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN`). Argo CD Image Updater then rolls the new
version out to the cluster. Pushes and pull requests run the tests (`.github/workflows/ci.yml`).
