# ASP.NET Core container

Build the image locally:

```powershell
docker buildx build --platform linux/amd64 --load --tag templateproject:local .
```

Run the API and optional SPA from one origin:

```powershell
docker run --rm --publish 8080:8080 templateproject:local
```

The image exposes port `8080`, maps `/health`, and declares an image health check.
When a React or Svelte frontend is selected, `/` serves its generated static assets.

The health endpoint is contributed only with container packaging. Aspire-composed
applications keep using their host-provided service-default health endpoints.
