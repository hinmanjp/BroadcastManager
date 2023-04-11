#!/usr/bin/env bash

# configure ssh to allow access
apt install -y ssh
cat << EOF >> /etc/ssh/sshd_config.d/permit_root.conf
PermitRootLogin prohibit-password
EOF

systemctl restart sshd


# generate public key from private key
# install public key in /root/.ssh/authorized_keys

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

###############################################################
#  START INSTALL OF DOTNET RUNTIME                            #
# Tell ubuntu to prefer Microsoft's .net packages over it's own
cat << EOF >> /etc/apt/preferences
Package: dotnet* aspnet* netstandard*
Pin: origin "archive.ubuntu.com"
Pin-Priority: -10
EOF

# Get Ubuntu version
declare repo_version=$(if command -v lsb_release &> /dev/null; then lsb_release -r -s; else grep -oP '(?<=^VERSION_ID=).+' /etc/os-release | tr -d '"'; fi)

# Download Microsoft signing key and repository
wget https://packages.microsoft.com/config/ubuntu/$repo_version/packages-microsoft-prod.deb -O packages-microsoft-prod.deb

# Install Microsoft signing key and repository
sudo dpkg -i packages-microsoft-prod.deb

# Clean up
rm packages-microsoft-prod.deb

# Update packages
sudo apt update

# install nginx, the rtmp mod, and .net core
apt install -y libnginx-mod-rtmp aspnetcore-runtime-7.0
##############################################################


apt install -y obs-studio gnome-screensaver

# prerequisites for docker
apt install apt-transport-https ca-certificates curl software-properties-common
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /usr/share/keyrings/docker-archive-keyring.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/docker-archive-keyring.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
apt update
apt-cache policy docker-ce
apt install docker-ce



curl -o /usr/bin/getssl --silent https://raw.githubusercontent.com/srvrco/getssl/latest/getssl ; chmod 771 /usr/bin/getssl

mkdir -p /opt/getssl/

curl -o /opt/getssl/dns_add_cloudflare --silent https://raw.githubusercontent.com/srvrco/getssl/master/dns_scripts/dns_add_cloudflare
curl -o /opt/getssl/dns_del_cloudflare --silent https://raw.githubusercontent.com/srvrco/getssl/master/dns_scripts/dns_del_cloudflare

chmod 771 /opt/getssl/*

getssl -c "*.$DOMAIN"

sed -i 's/PRIVATE_KEY_ALG="rsa"/PRIVATE_KEY_ALG="prime256v1"/' /root/.getssl/getssl.cfg
sed -i 's/SERVER_TYPE="https"/#SERVER_TYPE="https"/' /root/.getssl/getssl.cfg
sed -i 's/CHECK_REMOTE="true"/CHECK_REMOTE="false"/' /root/.getssl/getssl.cfg

TIMESTAMP=$(date)
TIMESTAMP=${TIMESTAMP// /_}
mv /root/.getssl/\*.$DOMAIN/getssl.cfg /root/.getssl/\*.$DOMAIN/getssl.cfg.$TIMESTAMP

cat << EOF > /root/.getssl/\*.$DOMAIN/getssl.cfg
CA="https://acme-v02.api.letsencrypt.org"
VALIDATE_VIA_DNS="true"
DNS_ADD_COMMAND="CF_API_TOKEN={CF_TOKEN} CF_ZONE_ID={CF_ZONE_ID} /opt/getssl/dns_add_cloudflare"
DNS_DEL_COMMAND="CF_API_TOKEN={CF_TOKEN} CF_ZONE_ID={CF_ZONE_ID} /opt/getssl/dns_del_cloudflare"
PUBLIC_DNS_SERVER="1.1.1.1"
AUTH_DNS_SERVER="1.1.1.1"
FULL_CHAIN_INCLUDE_ROOT="true"
DOMAIN_CERT_LOCATION="/etc/ssl/$DOMAIN.crt" 
DOMAIN_KEY_LOCATION="/etc/ssl/$DOMAIN.key"
CA_CERT_LOCATION="/etc/ssl/ca_chain.crt" 
DOMAIN_CHAIN_LOCATION="/etc/ssl/$DOMAIN.chain.crt"
DOMAIN_PEM_LOCATION="/etc/ssl/$DOMAIN.full.pem" 
RELOAD_CMD="openssl pkcs12 -export -nodes -out /etc/ssl/$DOMAIN.pfx -inkey /etc/ssl/$DOMAIN.key -in /etc/ssl/$DOMAIN.chain.crt -passout pass:  ; chmod 640 /etc/ssl/*.{key,pem,pfx} ; chown :ssl-cert /etc/ssl/*.{key,pem,pfx}"

EOF

#sed -ri 's/^(SANS=")/#\1/' /root/.getssl/\*.$DOMAIN/getssl.cfg

# generate & install the certificates
getssl -a


cat << EOF > /etc/cron.d/cert_renewal
# run certificate renewal check daily at 08:03 
03 08 * * * root /usr/bin/getssl -a -q -u 
EOF

apt -y install stunnel4

mkdir -p /var/log/stunnel4

chown stunnel4:stunnel4 /var/log/stunnel4

cat << EOF > /etc/stunnel/rtmp_out.conf
; **************************************************************************
; * Global options                                                         *
; **************************************************************************

; It is recommended to drop root privileges if stunnel is started by root
setuid = stunnel4
setgid = stunnel4

; PID file is created inside the chroot jail (if enabled)
pid = /tmp/stunnel4-rtmp-out.pid

; Debugging stuff (may be useful for troubleshooting)
;foreground = no
;debug = info
;output = /var/log/stunnel4/rtmp-out.log

; Enable FIPS 140-2 mode if needed for compliance
;fips = yes

;include = /etc/stunnel/conf.d


[rtmp-out]
client = yes
accept = 127.0.0.1:19353
connect = 127.0.0.1:1936
verifyChain = yes
CApath = /etc/ssl/certs
checkHost = $DOMAIN
#OCSPaia = yes
EOF


# for if this exists first....
cat << EOF >>  /etc/nginx/nginx.conf

rtmp {
        server {
                listen 127.0.0.1:19351 proxy_protocol;
                chunk_size 4096;
                allow publish 127.0.0.1;
                #deny publish all;
                deny play all;

                application live {
                        live on;
                        record off;

                        push rtmp://127.0.0.1:19352/app/sac1;
                        push rtmp://127.0.0.1:19353/live/sac1;
                }
        }
}
EOF

cat << EOF > /tmp/ometls.txt

                                <TLS>
                                        <CertPath>/etc/ssl/$DOMAIN.chain.crt</CertPath>
                                        <KeyPath>/etc/ssl/$DOMAIN.key</KeyPath>
                                </TLS>
EOF

perl -i -p0e 's/\s+<!--\n\s+<TLS>.*?<\/TLS>\n\s+-->\n/`cat \/tmp\/ometls.txt`/se' /opt/ome/origin.conf.xml


docker run --restart=always --name ome -d -e OME_HOST_IP=test.$DOMAIN \
-p 19352:1935 -p 9999:9999/udp -p 9000:9000 -p 3333:3333 -p 3334:3334 -p 3478:3478 -p 10000-10009:10000-10009/udp \
-v /etc/ssl/$DOMAIN.chain.crt:/etc/ssl/$DOMAIN.chain.crt \
-v /etc/ssl/$DOMAIN.key:/etc/ssl/$DOMAIN.key \
-v /opt/ome/origin.conf.xml:/opt/ovenmediaengine/bin/origin_conf/Server.xml \
airensoft/ovenmediaengine:latest
#-v /opt/ovenmediaengine/edge.conf.xml:/opt/ovenmediaengine/bin/edge_conf/Server.xml \

# create a directory of the user that OBS will run under
mkdir -p /home/***REMOVED***/.config/autostart/

# lock the desktop on login
cat << EOF > /home/***REMOVED***/.config/autostart/gnome-screensaver-command.desktop
[Desktop Entry]
Type=Application
Exec=/usr/bin/gnome-screensaver-command -l
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
Name[en_US]=a
Name=z
Comment[en_US]=
Comment=
EOF

# start OBS on login
cat << EOF > /home/***REMOVED***/.config/autostart/obs.desktop
[Desktop Entry]
Type=Application
Exec=/usr/bin/obs
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
Name[en_US]=obs
Name=obs
Comment[en_US]=
Comment=
EOF

cat << EOF > /etc/dconf/profile/user
user-db:user
system-db:local
EOF

mkdir -p /etc/dconf/db/local.d/
cat << EOF > /etc/dconf/db/local.d/00-logout 
[org/gnome/desktop/lockdown]
# Prevent the user from logging out
disable-log-out=true

# Prevent the user from user switching
disable-user-switching=true
EOF

cat << EOF > /etc/dconf/db/local.d/locks/lockdown
# Lock user logout
/org/gnome/desktop/lockdown/disable-log-out

# Lock user switching
/org/gnome/desktop/lockdown/disable-user-switching
EOF

dconf update


# enable autologin for user (thus starting OBS, which cannot run headless at this time)
sed -ri "s/^\s*#\s*AutomaticLoginEnable\s*=.*/AutomaticLoginEnable = true/" /etc/gdm3/custom.conf
sed -ri "s/^\s*#\s*AutomaticLogin\s*=.*/AutomaticLogin = ***REMOVED***/" /etc/gdm3/custom.conf

# make it possible to use VNC
sed -ri "s/^\s*#\s*WaylandEnable\s*=.*/WaylandEnable=false/" /etc/gdm3/custom.conf



# create a user account for running the Broadcast Manager app
adduser --system --quiet --no-create-home --disabled-password --disabled-login --gecos "" web-app

# grant the broadcast manager app access to ssl key files
usermod -a -G ssl-cert web-app


# Download & deploy the latest Broadcast Manager release from github
# deploy OBS scenes & configure streaming
# admin will need to configure camera source


# CREATE OR INSTALL SSH Keys in both root and Stake Admin directories


# Configure VNC server for remote configuration
apt install -y xfce4 xfce4-goodies
apt install -y tightvncserver expect


PROG=$(/usr/bin/which vncpasswd) NEWPASS="Sacramen"; sudo -u ***REMOVED*** --preserve-env=PROG --preserve-env=NEWPASS /usr/bin/expect <<EOF
spawn "$PROG"
expect "Password:"
send "$NEWPASS\r"
expect "Verify:"
send "$NEWPASS\r"
expect "Would you like to enter a view-only password (y/n)?"
send "\r"
expect eof
exit
EOF

sudo -u ***REMOVED*** vncserver
sudo -u ***REMOVED*** vncserver -kill :1
sudo -u ***REMOVED*** mv ~/.vnc/xstartup ~/.vnc/xstartup.bak

sudo -u ***REMOVED*** -i cat << EOF > /home/***REMOVED***/.vnc/xstartup
#!/bin/bash
xrdb $HOME/.Xresources
startxfce4 &
EOF

chmod +x /home/***REMOVED***/.vnc/xstartup

# DO THIS IN CODE ON START
cat << EOF > /opt/start-obs.sh
#!/usr/bin/bash
if ! pgrep -x obs >/dev/null
then
echo no obs!
DISPLAY=:0 sudo --preserve-env=DISPLAY -u ***REMOVED*** obs &
fi
EOF

chown www-data /opt/start-obs.sh
chmod 774 /opt/start-obs.sh


# Configure VPN client for remote configuration



