#!/bin/sh

# Check host configuration
sysctl net.ipv4.conf.all.src_valid_mark

# Set host src_valid_mark temporary (until reboot)
sudo sysctl -w net.ipv4.conf.all.src_valid_mark=1

# Set host src-valid-mark permanent
sudo mkdir -p /etc/sysctl.d && \
  echo 'net.ipv4.conf.all.src_valid_mark = 1' | sudo tee /etc/sysctl.d/99-src-valid-mark.conf && \
  sudo sysctl --system
