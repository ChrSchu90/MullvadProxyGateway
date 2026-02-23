# Mullvad Proxy Gateway

[![Build](https://github.com/ChrSchu90/MullvadProxyGateway/actions/workflows/build.yml/badge.svg)](https://github.com/ChrSchu90/MullvadProxyGateway/actions/workflows/build.yml)

> [!Caution]
> Project is mid development and not yet ready for production use.
> Wait for the first stable release before using it!

Turn a single Mullvad WireGuard client into a shared SOCKS5 proxy server that lets you connect to any city provided by Mullvad. 
Route the traffic from any device or application through it and connect seamlessly to any Mullvad location.

<img height="180" src="https://github.com/user-attachments/assets/ec9d8b72-6827-4289-b076-c8b40f50fdd9" />

## Features ✔️
- ✅ Container healthcheck
- ✅ Local SOCKS5 proxy
- ✅ Dedicated proxy endpoints per Mullvad city (up to 9 per city)
- ✅ Random server pool per Mullvad city
- ✅ Updatable server list (configurable)
- ✅ Configurable users
- ✅ Configurable bypasses (route traffic locally for defined URLs)
- ❌ Export proxy list as CSV/JSON
- ❌ Multiple WireGuard configurations as fallback

## How it works 🏗️
The container uses `GostGen` to create or update the `gost.yaml` configuration for the [Gost proxy server](https://gost.run/en).
Because a large number of proxy endpoints is generated, the resulting configuration can become very large (12k+ lines).
Available servers are fetched from the [Mullvad Relay API](https://api.mullvad.net/www/relays/wireguard) to keep endpoints up to date.

After that, `wg-quick` starts the WireGuard connection using `wg0-mullvad.conf`.
This connection provides access to the Mullvad network, where SOCKS5 proxies are used as exit nodes.
Connection status is monitored via a healthcheck against [Mullvad Connection Check](https://am.i.mullvad.net/json).

Finally, [gost](https://gost.run/en) starts with the generated `gost.yaml`. 
Clients connect to the container IP and choose the desired location by using the corresponding city port.

## Setup 🛠️

### Data volume 📁
A `data` volume must be attached to the container.
The following configuration **files are required to run the container**:
- WireGuard configuration (`wg0-mullvad.conf`)
- Gateway configuration (`gateway.yaml`)
 
### Mullvad WireGuard config 🔐
> [!TIP]
> Use the **nearest available location** for the VPN connection. 
> The VPN is only needed to access the Mullvad network in order 
> to obtain the SOCKS5 proxies, which will then be used as the 
> exit nodes for the traffic.

[Generate a WireGuard config](https://mullvad.net/en/account/wireguard-config)

Example `wg0-mullvad.conf`:
```ini
[Interface]
PrivateKey = YOUR_PRIVATE_KEY_HERE
Address = 10.64.123.45/32, fc00:bbbb:bbbb:bb01::5:7b2d/128
DNS = 10.64.0.1

[Peer]
PublicKey = SERVER_PUBLIC_KEY_HERE
AllowedIPs = 0.0.0.0/0, ::/0
Endpoint = se-sto-wg-001.mullvad.net:51820
PersistentKeepalive = 25
```

### Gateway config 🤖
Configuration file options for the Gost Config Generator:
| Name                           | Default       | Description                                                  | Allowed values                                                   |
| ------------------------------ | ------------- | ------------------------------------------------------------ | ---------------------------------------------------------------- |
| GeneratorLogLevel              | `Information` | Logging level of Gost Config Generator                       | `Verbose`, `Debug`, `Information`, `Warning`, `Error` or `Fatal` |
| GeneratorAlwaysGenerateServers | `false`       | If `true` updates the proxy servers on every container start | `true` / `false`                                                 |
| GostLogLevel                   | `warn`        | Logging level of Gost proxy server                           | `trace`, `debug`, `info`, `warn`, `error` or `fatal`             |

Example `gateway.yaml`:
```yaml
GeneratorLogLevel: Information
GeneratorAlwaysGenerateServers: false
GostLogLevel: warn
Users:
  User1:
    Password: Password1
  User2:
    Password: Password2
Bypasses:
- 'example.com'
- '*.example.com'
```

### Docker examples 🐳
#### Compose 🧩:
```yaml
services:
  mullvad-proxy-gateway:
    image: ghcr.io/chrschu90/mullvad-proxy-gateway:latest
    container_name: mullvad-proxy-gateway
    restart: unless-stopped
    ports:
      - "1080:1080"             # Local proxy
      - "2000-3000:2000-3000"   # Dynamic Mullvad proxies
    volumes:
      - mullvad-proxy-gateway_data:/data
    cap_add:
      - NET_ADMIN               # Requirement for WireGuard client
    sysctls:
      net.ipv4.conf.all.src_valid_mark: 1 # Requirement for WireGuard client
    environment:
      TZ: Europe/Berlin         # Update to your timezone!
volumes:
  mullvad-proxy-gateway_data:
    name: mullvad-proxy-gateway_data
```
#### CLI 💻:
```bash
docker volume create mullvad-proxy-gateway_data && \
docker run -d \
  --name mullvad-proxy-gateway \
  --restart unless-stopped \
  -p 1080:1080 \
  -p 2000-3000:2000-3000 \
  -v mullvad-proxy-gateway_data:/data \
  --cap-add=NET_ADMIN \
  --sysctl net.ipv4.conf.all.src_valid_mark=1 \
  -e TZ=Europe/Berlin \
  ghcr.io/chrschu90/mullvad-proxy-gateway:latest
```

## Exports 📤
