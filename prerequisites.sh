#!/usr/bin/env bash

# getssl certificate management
# OBS
#  - configured to capture from HDMI input
#  - stream to local nginx
# docker install of OvenMediaEngine?
#  - ome configs
#  - start ome image on boot
# stunnel4 - for rtmp encryption
# nginx + rtmp module - for split push to OvenMediaEngine & remote server
# BroadcastManager
# wireguard vpn - for remote management
# vnc / rdp - remote management
# gnome screen locking 

# login scripts
#  - start OBS
#  - lock screen

# need input of Let's Encrypt credentials
# need input of domain to be used 
# need input of cloudflare token key for dns management
# need input of ssh private key
#  - generate public key from private key

add-apt-repository -y ppa:obsproject/obs-studio


apt -y install stunnel4

mkdir /var/{run,log}/stunnel4

chown stunnel4:stunnel4 {/var/run/stunnel4,/var/log/stunnel4}

cat << EOF > /etc/stunnel/rtmp_out.conf
; **************************************************************************
; * Global options                                                         *
; **************************************************************************

; It is recommended to drop root privileges if stunnel is started by root
setuid = stunnel4
setgid = stunnel4

; PID file is created inside the chroot jail (if enabled)
pid = /var/run/stunnel4/rtmp-out.pid

; Debugging stuff (may be useful for troubleshooting)
;foreground = no
;debug = info
;output = /var/log/stunnel4/rtmp-out.log

; Enable FIPS 140-2 mode if needed for compliance
;fips = yes

;include = /etc/stunnel/conf.d


[rtmp-out]
client = yes
accept = 127.0.0.1:19352
connect = 127.0.0.1:1936
verifyChain = yes
CApath = /etc/ssl/certs
checkHost = willowbrook-ward.org
#OCSPaia = yes
EOF


