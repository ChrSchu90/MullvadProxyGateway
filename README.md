# Mullvad Proxy Gateway

[![Build](https://github.com/ChrSchu90/MullvadProxyGateway/actions/workflows/build.yml/badge.svg)](https://github.com/ChrSchu90/MullvadProxyGateway/actions/workflows/build.yml)

> [!Caution]
> Project is mid development and not yet ready for production use.
> Wait for the first stable release before using it!

Turn a single Mullvad WireGuard client into a shared SOCKS5 proxy server that lets you connect to any city provided by Mullvad. 
Route the traffic from any device or application through it and connect seamlessly to any Mullvad location.

## Features ✔️
- &#10003; Container healthcheck
- &#10003; Proxy server for local connection
- &#10003; Proxy server for each Mullvad City (max 9 per City)
- &#10003; Random server pool for each Mullvad City
- &#10003; Update of available servers (configurable)
- &#10003; Configurable users
- &#10003; Configurable bypasses (redirect Mullvad traffic local based on URL)
- &#10007; Export of proxy list as CSV/Json
- &#10007; Multiple WireGuard configs as connect fallback

## How it works 🏗️
The container uses `GostGen` to generate/update the [Gost proxy server](https://gost.run/en) config `gost.yaml`. 
Due to the large number of proxy servers, the resulting gost configuration becomes highly complex, exceeding 12k+ lines.
The generator uses the [Mullvad API](https://api.mullvad.net/www/relays/wireguard) to optain the available servers, to ensure they are up to date.

Afterwards, `wg-quick` is used to start the WireGuard client `wg0-mullvad.conf` connection to Mullvad VPN. 
This provides access to the Mullvad network, allowing retrieval of the SOCKS5 proxies, which are then used as exit nodes for the traffic.
The connection is monitored by healthcheck against [Mullvad Connection Check](https://am.i.mullvad.net/json).

The generated `gost.yaml` file is then used to start the [Gost proxy server](https://gost.run/en), 
which tunnels the traffic to the Mullvad proxy exit nodes. Clients can connect to any Mullvad city exit node 
by using the Docker container as the proxy server address and selecting the corresponding city-specific port to choose the desired exit location.

## Setup 🛠️

### Data volume 📁
The `data` volume contains the configuration for the WireGuard Client (`wg0-mullvad.conf`) and for the
gost config generator (`gateway.yaml`).
 
### Mullvad WireGuard config 🔐
> [!TIP]
> Use the **nearest available location** for the VPN connection. 
> The VPN is only needed to access the Mullvad network in order 
> to obtain the SOCKS5 proxies, which will then be used as the 
> exit nodes for the traffic.

To generate a config visit: https://mullvad.net/en/account/wireguard-config

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

### Gost Config Generator 🤖
Configuration file for the Gost Config Generator.
| Name                           | Default       | Description                                                  | Limits                                                           |
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
