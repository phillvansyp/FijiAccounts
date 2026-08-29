# Production deployment

The production application and its dedicated Caddy proxy run on the `hercules`
Linux Docker host and are published at `https://app.accountisland.com`:

- Docker host: `192.168.1.125` (`hercules`), application port `8188`.
- App-owned Caddy: internal ports `8280` (HTTP) and `8443` (HTTPS).
- Public address: `222.154.228.115`.

Every push to `main` runs the test suite, builds an immutable image, backs up the
SQLite data directory, and deploys it. If the application health check on the
Docker host fails, the workflow starts the previous image again. After a
successful deployment, a separate GitHub-hosted job checks the public HTTPS
endpoint so DNS, router forwarding, Caddy, TLS, and the application are covered.
A public-check failure marks the workflow failed but does not automatically roll
back an application that passed its internal health check.

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

The deployment workflow can provision these values from GitHub's `production`
environment secrets. Use the names `EMAIL_FROM_ADDRESS`, `EMAIL_SMTP_HOST`,
`EMAIL_SMTP_PORT`, `EMAIL_SMTP_USERNAME`, and `EMAIL_SMTP_PASSWORD`. All five
must be present; a partial configuration stops the deployment.

```dotenv
PlatformAdmin__Email=admin@example.com
Email__FromAddress=accounts@example.com
Email__Smtp__Host=smtp.example.com
Email__Smtp__Port=587
Email__Smtp__UseSsl=true
Email__Smtp__TimeoutMilliseconds=15000
Email__Smtp__Username=example-user
Email__Smtp__Password=replace-me
```

## Mobile OAuth activation

Mobile OAuth is disabled by default. Before enabling it, create separate RSA
signing and encryption certificates in the existing protected keys directory.
The private keys and their passwords must never be committed.

```bash
cd /mnt/data/account-island/keys
openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 730 \
  -subj "/CN=Account Island OAuth signing" \
  -keyout oauth-signing.key -out oauth-signing.crt
openssl pkcs12 -export -out oauth-signing.pfx \
  -inkey oauth-signing.key -in oauth-signing.crt
openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 730 \
  -subj "/CN=Account Island OAuth encryption" \
  -keyout oauth-encryption.key -out oauth-encryption.crt
openssl pkcs12 -export -out oauth-encryption.pfx \
  -inkey oauth-encryption.key -in oauth-encryption.crt
chmod 0600 oauth-*.key oauth-*.pfx
```

Add the following production settings to `app.env` after the iOS universal
link and Android app link callbacks are associated with the released apps:

```dotenv
MobileAuthentication__Enabled=true
MobileAuthentication__ClientId=account-island-mobile
MobileAuthentication__IosRedirectUri=https://app.accountisland.com/mobile/callback/ios
MobileAuthentication__AndroidRedirectUri=https://app.accountisland.com/mobile/callback/android
MobileAuthentication__SigningCertificatePath=/app/keys/oauth-signing.pfx
MobileAuthentication__SigningCertificatePassword=replace-me
MobileAuthentication__EncryptionCertificatePath=/app/keys/oauth-encryption.pfx
MobileAuthentication__EncryptionCertificatePassword=replace-me
```

Back up the certificates and passwords separately from the application host.
Certificate rotation must overlap old and new validation keys long enough for
issued access tokens to expire.

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

The application owns the Caddy service declared in `deploy/compose.yml`, its
configuration, and its certificate storage. Other application deployments do
not modify it. Configure the router with these translations on the Hercules
public connection:

- WAN port `80` to `192.168.1.125:8280`.
- WAN port `443` to `192.168.1.125:8443`.

Set the `app.accountisland.com` A record to `222.154.228.115`. Because the
router exposes nonstandard public ports, the TLS certificate uses a manual DNS
challenge and must be renewed before its expiry date. Caddy serves the issued
certificate from `/etc/letsencrypt`.

## Operations

The deployment workflow has two distinct health checks:

- The self-hosted deployment job checks `192.168.1.125:8188` and rolls the
  application image back if that internal check fails.
- A GitHub-hosted job then checks `https://app.accountisland.com/health` from
  outside the production network. If it fails, inspect public DNS, router port
  forwarding, Caddy, and the TLS certificate before considering an application
  rollback.

```bash
cd /mnt/data/account-island/stack
docker compose ps
docker compose logs --tail 200 app
docker compose logs --tail 200 caddy
curl --fail https://app.accountisland.com/health
```

Backups are stored in `/mnt/data/account-island/backups` and retained for 30
days. Copy them to separate storage; a backup on the same machine is not
sufficient for disaster recovery.
