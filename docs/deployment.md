# Production deployment

The production application runs on the existing `rigpilot-apps` Linux Docker
host and is published at `https://app.accountisland.com` through the separate
LAN Caddy gateway:

- Docker host: `192.168.1.125` (`hercules`), application port `8188`.
- Caddy gateway: `192.168.1.129`, which receives public ports 80 and 443.

Every push to `main` runs the test suite, builds an immutable image, backs up the
SQLite data directory, and deploys it. If the public health check fails, the
workflow starts the previous image again.

## One-time server preparation

The Docker host is `hercules` (`192.168.1.125` on the LAN). Public SSH is
available at `222.154.228.115:22`. Outbound HTTPS to GitHub and container
registries must remain allowed.

Git and Docker Engine are already present. Register the runner under the
existing `serv1` account, then prepare the deployment directories on the data
volume:

```bash
install -d -m 0750 /mnt/data/account-island/{stack,config,data,keys,backups}
touch /mnt/data/account-island/config/app.env
chmod 0600 /mnt/data/account-island/config/app.env
```

Add production-only settings to `/mnt/data/account-island/config/app.env`. Use
double underscores for nested ASP.NET Core configuration keys. Do not commit
this file.

```dotenv
PlatformAdmin__Email=admin@example.com
Email__FromAddress=accounts@example.com
Email__Smtp__Host=smtp.example.com
Email__Smtp__Port=587
Email__Smtp__UseSsl=true
Email__Smtp__Username=example-user
Email__Smtp__Password=replace-me
```

## Register the self-hosted runner

In the GitHub repository, open **Settings > Actions > Runners > New self-hosted
runner**, choose **Linux** and **x64**, and run GitHub's displayed commands as
`serv1` in `/mnt/data/account-island/runner`. The registration
token is time-limited; never save or commit it. Give the runner the additional
label `account-island` when prompted.

After registration, install it as a boot service:

```bash
cd /mnt/data/account-island/runner
sudo ./svc.sh install serv1
sudo ./svc.sh start
sudo ./svc.sh status
```

The repository should be private because a deployment runner has access to the
Docker daemon and production data. Protect `main`, restrict who can approve the
`production` environment, and do not enable workflows from untrusted forks.

## First data migration

The database is deliberately excluded from Git. Before the first deployment,
copy the existing `app.db` to the server as
`/mnt/data/account-island/data/account-island.db`, while the application is
stopped, then set ownership to container user `1654:1654`. If no database is
copied, the first deployment creates a new empty database and applies all
migrations.

## Caddy gateway

The application deployment does not access or modify the gateway. Through the
gateway's existing authorized administration path, install the route in
`deploy/Caddyfile` and reload Caddy. It terminates TLS and proxies to the
Hercules LAN endpoint `192.168.1.125:8188`.

## Operations

```bash
cd /mnt/data/account-island/stack
docker compose ps
docker compose logs --tail 200 app
curl --fail https://app.accountisland.com/health
```

Backups are stored in `/mnt/data/account-island/backups` and retained for 30
days. Copy them to separate storage; a backup on the same machine is not
sufficient for disaster recovery.
