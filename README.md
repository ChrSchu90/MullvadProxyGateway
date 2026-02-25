# Mullvad Proxy Gateway

[![Build](https://github.com/ChrSchu90/MullvadProxyGateway/actions/workflows/build.yml/badge.svg)](https://github.com/ChrSchu90/MullvadProxyGateway/actions/workflows/build.yml)

Turn a single Mullvad WireGuard client into a shared SOCKS5 proxy server that lets you connect to any city provided by Mullvad. 
Route the traffic from any device or application through it and connect seamlessly to any Mullvad location.

<img height="180" src="https://github.com/user-attachments/assets/ec9d8b72-6827-4289-b076-c8b40f50fdd9" />

## Features ✔️

- ✅ Container healthcheck
- ✅ Local SOCKS5 proxy
- ✅ Dedicated SOCKS5 proxy server per [Mullvad city](https://mullvad.net/en/servers?type=wireguard) (up to 9 per city)
- ✅ One SOCKS5 proxy server pool per [Mullvad city](https://mullvad.net/en/servers?type=wireguard) with endpoint rotation
- ✅ Filter Mullvad proxies by country and city or ownership (rented/owned) 
- ✅ Updatable server list on container start (optional)
- ✅ Configurable users
- ✅ User roles (Mullvad proxy, local proxy and metrics)
- ✅ Configurable bypasses (route traffic locally for defined URLs)
- ✅ Export proxy list as CSV and JSON
- ✅ [GOST Prometheus Metrics](https://gost.run/en/tutorials/metrics/) (optional)
- ✅ Multiple WireGuard configurations with connection check on container start

## How it works 🏗️

The container uses `GostGen` to create or update the `gost.yaml` configuration for the [GOST proxy server](https://gost.run/en).
Because a large number of proxy endpoints is generated, the resulting configuration can become very large (12k+ lines).
Available servers are fetched from the [Mullvad Relay API](https://api.mullvad.net/www/relays/wireguard) to keep endpoints up to date.

Servers will be automatically added or updated when the container starts if AlwaysGenerateServers is enabled (disabled by default).
If a new server is added to the Mullvad network, it will be assigned the next available port number after the last existing one. 
This prevents changes to the port assignments of existing locations.

After that, `wg-quick` starts the WireGuard connection.
This connection provides access to the Mullvad network, where SOCKS5 proxies are used as exit nodes.
The container connection status is monitored via healthcheck against [Mullvad Connection Check](https://am.i.mullvad.net/json).

Finally, [GOST](https://gost.run/en) is started with the generated configuration. It provides a local SOCKS5 proxy as 
well as proxy endpoints for all available Mullvad locations worldwide. Clients can connect to the container’s IP address 
and select the desired target location by using the corresponding city-specific port.

After connecting to the proxy, you can verify the connection by visiting the
[Mullvad Connection check](https://mullvad.net/en/check) in your browser.

## Setup 🛠️

### Data volume 📁

A `data` volume must be mounted to the container.
The following configuration **files are required to run the container**:
- [WireGuard configuration(s)](#mullvad-wireguard-config-) (`*.conf`)
- [Gateway configuration](#gateway-config-) (`gateway.yaml`)
 
### Mullvad WireGuard config 🔐

> [!TIP]
> Use the **nearest available location** for the VPN connection. 
> The VPN is only needed to access the Mullvad network in order 
> to obtain the SOCKS5 proxies, which will then be used as the 
> exit nodes for the traffic.

Open the [WireGuard configuration file generator](https://mullvad.net/en/account/wireguard-config) 
and download multiple configuration files (for example, for Germany – Frankfurt).

Place the downloaded configuration files in the data folder.
Please note that the file names determine the order in which the connections are attempted, so name them accordingly (e.g., de-fra-wg-001.conf, de-fra-wg-002.conf, de-fra-wg-003.conf, etc.).

You may also include configurations for different locations. The first successfully working configuration will be used.

Example `de-fra-wg-001.conf`:
```ini
[Interface]
PrivateKey = YOUR_PRIVATE_KEY_HERE
Address = 10.0.0.5/32, bbbb:bbbb:bbbb:bbbb::5:bbbb/128
DNS = 10.64.0.1

[Peer]
PublicKey = SERVER_PUBLIC_KEY_HERE
AllowedIPs = 0.0.0.0/0, ::/0
Endpoint = de-fra-wg-001.mullvad.net:51820
```

### Gateway config 🤖

Example `gateway.yaml`:
```yaml
LogLevel: Information               # Logging level (Verbose, Debug, Information, Warning, Error or Fatal)
AlwaysGenerateServers: false        # Always update the proxy server list on container start if true
GostMetricsEnabled: false           # Enable GOST Metrics (Prometheus endpoint on port 9100)
Users:                              # List of users with access to the proxy and their permissions
  User1:                            # Name of the user (can be freely chosen)
    Password: Password1             # Password for the user (can be freely chosen)
    HasMullvadProxyAccess: true     # Access to the Mullvad proxies
    HasInternalProxyAccess: false   # Access to the local proxy
    HasMetricsAccess: false         # Access to the GOST metrics
  User2:
    Password: Password2
    HasMullvadProxyAccess: true
    HasInternalProxyAccess: false
    HasMetricsAccess: false
Bypasses:                           # Optional: List of URLs that bypasses the Mullvad proxies and are routed through the local connection instead
- 'example.com'
- '*.example.com'
ProxyFilter:                        # Optional: Proxy server filter
  OwnedOnly: false                  # Optional: Only include proxies from owned locations (no rented servers)
  Country:
    Include: ["de", "ch", "nl"]     # Optional: Include only specific countries (country codes or names)
    Exclude: []                     # Optional: Exclude specific countries (country codes or names)
  City:
    Include: []                     # Optional: Include only specific cities (city codes or names)
    Exclude: ["dus", "ber"]         # Optional: Exclude specific cities (city codes or names)
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
      - "9100:9100"             # Prometheus Metrics (optional)
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
  -p 9100:9100 \
  -p 2000-3000:2000-3000 \
  -v mullvad-proxy-gateway_data:/data \
  --cap-add=NET_ADMIN \
  --sysctl net.ipv4.conf.all.src_valid_mark=1 \
  -e TZ=Europe/Berlin \
  ghcr.io/chrschu90/mullvad-proxy-gateway:latest
```

## Exports 📤
To easyly generate importable proxy lists for other applications, the container exports the available Mullvad proxies as CSV and JSON files.
Sice the container does not know the external IP address, the export can't include the local IP with port.

📄 CSV example `data/proxies.csv`:

| Country  | Country Code | City   | City Code | City No. | Location Code | Port | Target                                  |
| -------- | -----------  | ------ | --------- | -------- | ------------- | ---- | --------------------------------------- |
| Albania  | al           | Tirana | tia       | 0        | al-tia        | 2000 | random                                  |
| Albania  | al           | Tirana | tia       | 1        | al-tia        | 2001 | al-tia-wg-socks5-003.relays.mullvad.net |
| Albania  | al           | Tirana | tia       | 2        | al-tia        | 2002 | al-tia-wg-socks5-004.relays.mullvad.net |

🗃️ JSON example `data/proxies.json`:

```json
[ 
  {"Country": "Albania", "CountryCode": "al", "City": "Tirana", "CityCode": "tia", "CityNo": 0, "LocationCode": "al-tia", "Port": 2000, "Target": "random"},
  {"Country": "Albania", "CountryCode": "al", "City": "Tirana", "CityCode": "tia", "CityNo": 1, "LocationCode": "al-tia", "Port": 2001, "Target": "al-tia-wg-socks5-003.relays.mullvad.net"},
  {"Country": "Albania", "CountryCode": "al", "City": "Tirana", "CityCode": "tia", "CityNo": 2, "LocationCode": "al-tia", "Port": 2002, "Target": "al-tia-wg-socks5-004.relays.mullvad.net"}
]
```
