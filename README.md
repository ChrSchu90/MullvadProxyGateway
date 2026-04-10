# Mullvad Proxy Gateway

[![Build](https://github.com/ChrSchu90/MullvadProxyGateway/actions/workflows/build.yml/badge.svg)](https://github.com/ChrSchu90/MullvadProxyGateway/actions/workflows/build.yml)

> [!NOTE]
> This is **not an official Mullvad project** and isn’t affiliated with or endorsed by **Mullvad VPN AB**.
>
> The name “Mullvad” is solely used to indicate the use and requirement of their services.
> All trademarks and service names belong to their respective owners.

The **Mullvad Proxy Gateway** transforms a single Mullvad WireGuard connection into a containerized SOCKS5 proxy platform.

Instead of running multiple VPN clients, this container establishes one secure WireGuard tunnel into the Mullvad network 
and exposes dedicated SOCKS5 endpoints for every available Mullvad city worldwide. Route traffic from any device 
or application through it and connect seamlessly to any Mullvad location — simply by selecting the corresponding proxy port.

For example, you can use your favorite browser proxy extension to switch between cities on the fly — instantly 
changing your exit location without reconnecting a VPN or restarting applications. Alternatively, configure 
only specific applications (e.g., a scraper or download client) to use the SOCKS5 proxy, while the rest of your 
system traffic continues to use your regular local connection. No full-device VPN routing required.

<img height="180" src="https://github.com/user-attachments/assets/ec9d8b72-6827-4289-b076-c8b40f50fdd9" />

## Features ✔️

- ✅ Container healthcheck
- ✅ Local SOCKS5 proxy
- ✅ Dedicated SOCKS5 proxy server per [Mullvad city](https://mullvad.net/en/servers?type=wireguard)
- ✅ One SOCKS5 proxy server pool per [Mullvad city](https://mullvad.net/en/servers?type=wireguard) with endpoint rotation
- ✅ Filter Mullvad proxies by country, city or ownership (rented/owned) 
- ✅ Updatable server list on container start (optional)
- ✅ Configurable users with roles (Mullvad proxy, local proxy and metrics)
- ✅ Configurable bypasses to route traffic locally for specific URLs (optional)
- ✅ Export of proxies as `CSV` and `JSON`
- ✅ [GOST Prometheus Metrics](https://gost.run/en/tutorials/metrics/) (optional)
- ✅ Multiple WireGuard configurations with connection check on container start

## How it works 🏗️

The container uses `GostGen` to create or update the `gost.yaml` configuration for the [GOST proxy server](https://gost.run/en).
Because a large number of proxy endpoints is generated, the resulting configuration can become very large (12k+ lines).
Available servers are fetched from the [Mullvad Relay API](https://api.mullvad.net/www/relays/wireguard) to keep endpoints up to date.

Servers will be automatically added or updated when the container starts if `UpdateServersOnStartup` is `true` (default `false`).
If a new server or city is added to the Mullvad network, it will be assigned to the next available port number after the last existing one. 
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

Place the downloaded configuration files in the `data` volume.
Note that the file names determine the order in which the connections are attempted, 
so name them accordingly (e.g., 01-de-fra.conf, 02-de-fra.conf, 03-de-fra.conf, etc.).

You may also include configurations for different locations. The first successfully working configuration will be used.

### Gateway config 🤖

> [!IMPORTANT]
> ***Do not change the `MaxServersPerCity`, `MullvadProxyPortStart` or `MullvadProxyPortEnd` value in production!***
>
> Modifying these values will shift the assigned container proxy ports.
> Before starting the container with modified values make sure to delete 
> the existing `gost.yaml` file. This ensures consistent ordering and 
> prevents endpoints from being reassigned incorrectly.

Example `gateway.yaml`:
```yaml
LogLevel: Information               # Logging level (Verbose, Debug, Information, Warning, Error or Fatal)
UpdateServersOnStartup: false       # Always update the proxy server list for GOST on container start, if true
MaxServersPerCity: 10               # Maximum amount of proxy endpoints per city
CityRandomPools: true               # Creates 1 proxy per city that randomly selects an exit node within that city (counts as 1 toward `MaxServersPerCity` and includes all available endpoints)
LocalProxyPort: 1080                # Port number for local proxy
MullvadProxyPortStart: 2000         # Start port number for dynamic Mullvad proxies
MullvadProxyPortEnd: 5000           # End port number for dynamic Mullvad proxies
GostMetricsEnabled: false           # Enable GOST Metrics (Prometheus endpoint) `curl -v -u user:password http://ip:9100/metrics`
GostMetricsPort: 9100               # Port number for GOST Metrics (Prometheus endpoint)
Users:                              # Optional: List of users with access to the proxy and their permissions
  User1:                            # Optional: Name of the user (can be freely chosen)
    Password: Password1             # Optional: Password for the user (can be freely chosen)
    HasMullvadProxyAccess: true     # Optional: Access to the Mullvad proxies
    HasInternalProxyAccess: false   # Optional: Access to the local proxy
    HasMetricsAccess: false         # Optional: Access to the GOST metrics
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
  Country:                          # Optional: Country filter settings
    Include: ["de", "ch", "nl"]     # Optional: Include only specific countries (country codes or names)
    Exclude: []                     # Optional: Exclude specific countries (country codes or names)
  City:                             # Optional: City filter settings
    Include: []                     # Optional: Include only specific cities (city codes or names)
    Exclude: ["dus", "ber"]         # Optional: Exclude specific cities (city codes or names)
```

### Docker examples 🐳

This image follows semantic versioning.
Use specific version tags for reproducibility. Preview tags are not recommended for production.

- `latest` – Most recent stable release
- `1` – Latest stable release in major version `1`
- `1.2` – Latest stable release in minor version `1.2`
- `1.2.3` – Specific stable patch version (fully pinned)
- `preview` – Latest preview build
- `1-preview` – Latest preview for major version `1`
- `1.2-preview` – Latest preview for minor version `1.2`
- `1.2.3-preview` – Latest preview for patch version `1.2.3`
- `1.2.3-beta.1` – Specific preview build (fully pinned)

> [!TIP]
> In certain setups, using the `bridge` network mode may lead to performance issues due to the large number of exposed ports.
> To mitigate this, consider switching to the `host` network mode instead.
> 
> When doing so, make sure to update the port configuration in `gateway.yaml` accordingly.
> Additionally, ensure that `net.ipv4.conf.all.src_valid_mark=1` is set on the host system (see [example](Examples/src_valid_mark.sh)).

#### Compose 🧩:

[Bridge Network](Examples/compose_bridge.yml)

[Host Network](Examples/compose_host.yml)

#### CLI 💻:

[Bridge Network](Examples/cli_bridge.sh)

[Host Network](Examples/cli_host.sh)

## Exports 📤
To easily generate importable proxy lists for other applications, the container exports the available Mullvad proxies as CSV and JSON files.
Since the container does't know the external IP address, the export can't include the proxy IP.

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
