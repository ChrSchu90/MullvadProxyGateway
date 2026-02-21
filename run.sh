#!/bin/sh

BLUE="\033[38;5;117m"
NC="\033[0m"

printf "\n\n\n${BLUE}############ Starting Gost Config Generator... ############${NC}\n"
./GostGen

printf "\n\n\n${BLUE}############ Starting WireGard VPN... ############${NC}\n"
resolvconf -a control 2>/dev/null < /etc/resolv.conf
resolvconf -u
wg-quick up /data/wg0-mullvad.conf
#curl https://am.i.mullvad.net/json

printf "\n\n\n${BLUE}############ Starting Proxies... ############${NC}\n"
./gost -C /data/gost.yaml