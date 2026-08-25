# End-to-end auth tests

Live integration tests that drive the **real** send pipeline (auth prestep → HTTP executor → captures)
against an **httpbin-compatible server**, which echoes back what it received. They prove that auth actually
reaches the wire, that credentials are not leaked across a cross-origin redirect, and that cookies are
isolated per environment — things a unit test with a fake HTTP handler can't fully prove.

We deliberately test against a **third-party** server (public httpbin / a Docker httpbin image) rather than
a hand-written mock: testing our own client against our own server would prove little.

## They are opt-in and NOT part of CI

Each test self-skips unless `FUBAR_E2E=1`, so the default `dotnet test` (and CI) stays offline and
deterministic. Run them **manually** when you add or change auth / HTTP behavior. A network blip should
never fail the normal suite.

## Run against the public services (zero setup)

```
FUBAR_E2E=1 dotnet test tests/Fubar.Studio.EndToEnd.Tests
```

Uses `https://httpbin.org` and `https://postman-echo.com` (the second, different-origin host is only used
by the cross-origin redirect tests).

## Run against local Docker (offline, deterministic)

```
docker compose -f tests/Fubar.Studio.EndToEnd.Tests/docker-compose.yml up -d

FUBAR_E2E=1 \
  FUBAR_E2E_BASEURL=http://localhost:8080 \
  FUBAR_E2E_OTHERHOST=http://localhost:8081/get \
  dotnet test tests/Fubar.Studio.EndToEnd.Tests

docker compose -f tests/Fubar.Studio.EndToEnd.Tests/docker-compose.yml down
```

Two httpbin containers are started; the second (a different port = a different origin) backs the
cross-origin redirect tests.

### Without the compose plugin (plain `podman run` / `docker run`)

If you don't have the `compose` subcommand, start the two containers directly (works the same):

```
podman run -d --rm -p 8080:80 --name hb1 kennethreitz/httpbin
podman run -d --rm -p 8081:80 --name hb2 kennethreitz/httpbin

FUBAR_E2E=1 \
  FUBAR_E2E_BASEURL=http://localhost:8080 \
  FUBAR_E2E_OTHERHOST=http://localhost:8081/get \
  dotnet test tests/Fubar.Studio.EndToEnd.Tests

podman rm -f hb1 hb2
```

(Swap `podman` for `docker` if you use Docker.)

## Configuration

| Env var | Default | Purpose |
| --- | --- | --- |
| `FUBAR_E2E` | *(unset → skip)* | Set to `1` to actually run these tests. |
| `FUBAR_E2E_BASEURL` | `https://httpbin.org` | Base URL of the httpbin-compatible server. |
| `FUBAR_E2E_OTHERHOST` | `https://postman-echo.com/get` | A different-origin echo endpoint for the redirect tests. |
