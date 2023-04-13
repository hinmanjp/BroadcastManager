#!/usr/bin/env bash

# local ports in use
# TCP 3333 & 3334 - OvenMediaEngine - web rtc out
# TCP 19351 - NGINX - RTMP IN
# TCP 19352 - OvenMediaEngine - RTMP IN
# TCP 19353 - STUNNEL - RTMP IN

# TODO 
# - curl lookup of cloudflare zone id for domain
# - dns over https to bypass getssl dns issues
# wireguard vpn documentation - for remote management


if [ "$UID" -eq 0 -o "$EUID" -eq 0 ]; then
  echo This script should NOT be run as root. >&2
  echo Please run this from the desktop of the user >&2
  echo that will be used for broadcast management >&2
  exit 1
fi

OBS_USER=$USER

# GET INPUT FOR....
DOMAIN=""
while [ -z "$DOMAIN" ]
do
  echo Enter the domain that users acess the broadcast at.
  echo Example: ward-domain.org or stake-domain.org
  read DOMAIN
done
DOMAIN=$(echo $DOMAIN | tr '[:upper:]' '[:lower:]')

CF_TOKEN=""
while [ -z "$CF_TOKEN" ]
do
  echo Enter the CloudFlare API token used to configure DNS records for $DOMAIN
  read CF_TOKEN
done

VULTR_TOKEN=""
while [ -z "$VULTR_TOKEN" ]
do
  echo Enter the Vultr API token used to configure the broadcast distribution server
  read VULTR_TOKEN
done

APP_MASTER_PW=""
while [ -z "$APP_MASTER_PW" ]
do
  echo Enter a master password for initial access to the Broadcast Manager
  read -s APP_MASTER_PW
done

SSH_PRIVATE_KEY=""
while [ -z "SSH_PRIVATE_KEY" ]
do
  echo Enter a private ssh key
  read SSH_PRIVATE_KEY
done

VNC_PASSWORD=""
while [ -z "$VNC_PASSWORD" ]
do
  echo Enter a password to be used for remote desktop access by VNC
  read -s VNC_PASSWORD
done

TIMEZONE=$(cat /etc/timezone)

# //generate public key from ssh private key
mkdir -p ~/.ssh
sudo mkdir -p /root/.ssh

SSH_PRIVATE_KEY_PATH=~/.ssh/broadcastmanager_ssh_key

echo $SSH_PRIVATE_KEY > $SSH_PRIVATE_KEY_PATH
ssh-keygen -f $SSH_PRIVATE_KEY_PATH $SSH_PRIVATE_KEY_PATH.pub
cat $SSH_PRIVATE_KEY_PATH.pub >> ~/.ssh/authorized_keys
cat $SSH_PRIVATE_KEY_PATH.pub | sudo tee -a /root/.ssh/authorized_keys > /dev/null

sudo chown -R root:root /root/.ssh
chmod 700 ~/.ssh
chmod 600 ~/.ssh/*

sudo chmod 700 /root/.ssh
shdo chmod 600 /root/.ssh/*


# configure ssh to allow access
sudo apt install -y ssh
cat << EOF | sudo tee -a /etc/ssh/sshd_config.d/permit_root.conf > /dev/null
PermitRootLogin prohibit-password
EOF

sudo systemctl restart sshd

sudo add-apt-repository -y ppa:obsproject/obs-studio

###############################################################
#  START INSTALL OF DOTNET RUNTIME                            #
# Tell ubuntu to prefer Microsoft's .net packages over it's own
cat << EOF | sudo tee /etc/apt/preferences.d/dotnet > /dev/null
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
sudo apt install -y libnginx-mod-rtmp aspnetcore-runtime-7.0
##############################################################

# install open broadcast server & the ability to start the screensaver from script
sudo apt install -y obs-studio gnome-screensaver

# start OBS to create initial config files, then kill it after configs have been created
obs &

while [ ! -f ~/.config/obs-studio/global.ini ]
do
  echo waiting for initial obs config generation...
  sleep 1
done

# wait until the global.ini file has the right data in it
while [ $(grep -c "\[OBSWebSocket]" ~/.config/obs-studio/global.ini) -eq 0 ]
do
  echo waiting for initial obs config generation...
  sleep 1
done

# just for safety...
sleep 1

#shutdown obs
killall obs

# create obs-studio basic config
# create an api key
OBS_APIKEY=$(echo $RANDOM | md5sum | head -c 20;)

# configure the obs websocket - enable it, set a password
perl -i -pe 'BEGIN{undef $/;} s/\\[OBSWebSocket\].*\\n\\n/[OBSWebSocket]\\nFirstLoad=false\\nServerEnabled=true\\nServerPort=4455\\nAlertsEnabled=false\\nAuthRequired=true\\nServerPassword=$OBS_APIKEY\\n\\n/smg' ~/.config/obs-studio/global.ini

# configure OBS resolution, vdieo bit rate, video codec, audio bit rate, recording destination (rtmp stream), recording output dimensions
cat << EOF > ~/.config/obs-studio/basic/profiles/Untitled/basic.ini
[General]
Name=Untitled

[Video]
BaseCX=1920
BaseCY=1080
OutputCX=1920
OutputCY=1080
FPSType=0
FPSCommon=20
ScaleType=bicubic

[Panels]
CookieId=137DE664FF57D4C2

[SimpleOutput]
VBitrate=1500
StreamEncoder=x264
RecQuality=Stream
ABitrate=96

[Output]
Mode=Advanced

[AdvOut]
Encoder=obs_x264
TrackIndex=1
RecType=FFmpeg
RecSplitFileType=Time
RecTracks=1
FLVTrack=1
FFOutputToFile=false
FFFormat=rtp
FFFormatMimeType=
FFVEncoderId=12
FFVEncoder=mpeg4
FFAEncoderId=65542
FFAEncoder=pcm_mulaw
FFAudioMixes=1
FFURL=rtmp://127.0.0.1:19351/live/sac1
FFVBitrate=1500
FFIgnoreCompat=false
FFABitrate=96
FFExtension=
RescaleRes=1920x1080
RecRescaleRes=1920x1080
FFRescaleRes=1920x1080
EOF

# configure streaming destination
cat << EOF > ~.config/obs-studio/basic/profiles/Untitled/service.json
{"settings":{"bwtest":false,"key":"sac1","server":"rtmp://127.0.0.1:19351/live/","use_auth":false},"type":"rtmp_custom"}
EOF

# configure streaming encoder settings
cat << EOF > ~.config/obs-studio/basic/profiles/Untitled/streamEncoder.json
{"bitrate":5000,"preset":"ultrafast","profile":"high","rate_control":"CBR","tune":"zerolatency"}
EOF

# configure basic OBS scenes. 
# The camera scene is created, but no video or audio source is linked to it.
# this must be done manually after this script is done.
cat << EOF > ~.config/obs-studio/basic/scenes/Untitled.json
{
	"current_program_scene": "Camera",
	"current_scene": "Camera",
	"current_transition": "Fade",
	"groups": [],
	"modules": {
		"auto-scene-switcher": {
			"active": false,
			"interval": 300,
			"non_matching_scene": "",
			"switch_if_not_matching": false,
			"switches": []
		},
		"output-timer": {
			"autoStartRecordTimer": false,
			"autoStartStreamTimer": false,
			"pauseRecordTimer": true,
			"recordTimerHours": 0,
			"recordTimerMinutes": 0,
			"recordTimerSeconds": 30,
			"streamTimerHours": 0,
			"streamTimerMinutes": 0,
			"streamTimerSeconds": 30
		},
		"scripts-tool": []
	},
	"name": "Untitled",
	"preview_locked": false,
	"quick_transitions": [
		{
			"duration": 300,
			"fade_to_black": false,
			"hotkeys": [],
			"id": 1,
			"name": "Cut"
		},
		{
			"duration": 300,
			"fade_to_black": false,
			"hotkeys": [],
			"id": 2,
			"name": "Fade"
		},
		{
			"duration": 300,
			"fade_to_black": true,
			"hotkeys": [],
			"id": 3,
			"name": "Fade"
		}
	],
	"saved_projectors": [],
	"scaling_enabled": false,
	"scaling_level": 0,
	"scaling_off_x": 0.0,
	"scaling_off_y": 0.0,
	"scene_order": [
		{
			"name": "Camera"
		},
		{
			"name": "Paused"
		},
		{
			"name": "Black"
		},
		{
			"name": "PreStart"
		},
		{
			"name": "End"
		}
	],
	"sources": [
		{
			"balance": 0.5,
			"deinterlace_field_order": 0,
			"deinterlace_mode": 0,
			"enabled": true,
			"flags": 0,
			"hotkeys": {
				"OBSBasic.SelectScene": []
			},
			"id": "scene",
			"mixers": 0,
			"monitoring_type": 0,
			"muted": false,
			"name": "Camera",
			"prev_ver": 486539266,
			"private_settings": {},
			"push-to-mute": false,
			"push-to-mute-delay": 0,
			"push-to-talk": false,
			"push-to-talk-delay": 0,
			"settings": {
				"custom_size": false,
				"id_counter": 6,
				"items": []
			},
			"sync": 0,
			"versioned_id": "scene",
			"volume": 1.0
		},
		{
			"balance": 0.5,
			"deinterlace_field_order": 0,
			"deinterlace_mode": 0,
			"enabled": true,
			"flags": 0,
			"hotkeys": {
				"OBSBasic.SelectScene": [],
				"libobs.hide_scene_item.Paused Message": [],
				"libobs.show_scene_item.Paused Message": []
			},
			"id": "scene",
			"mixers": 0,
			"monitoring_type": 0,
			"muted": false,
			"name": "Paused",
			"prev_ver": 486539266,
			"private_settings": {},
			"push-to-mute": false,
			"push-to-mute-delay": 0,
			"push-to-talk": false,
			"push-to-talk-delay": 0,
			"settings": {
				"custom_size": false,
				"id_counter": 1,
				"items": [
					{
						"align": 5,
						"blend_method": "default",
						"blend_type": "normal",
						"bounds": {
							"x": 0.0,
							"y": 0.0
						},
						"bounds_align": 0,
						"bounds_type": 0,
						"crop_bottom": 0,
						"crop_left": 0,
						"crop_right": 0,
						"crop_top": 0,
						"group_item_backup": false,
						"hide_transition": {
							"duration": 0
						},
						"id": 1,
						"locked": false,
						"name": "Paused Message",
						"pos": {
							"x": 548.0,
							"y": 392.0
						},
						"private_settings": {},
						"rot": 0.0,
						"scale": {
							"x": 1.0,
							"y": 1.0
						},
						"scale_filter": "disable",
						"show_transition": {
							"duration": 0
						},
						"visible": true
					}
				]
			},
			"sync": 0,
			"versioned_id": "scene",
			"volume": 1.0
		},
		{
			"balance": 0.5,
			"deinterlace_field_order": 0,
			"deinterlace_mode": 0,
			"enabled": true,
			"flags": 0,
			"hotkeys": {
				"OBSBasic.SelectScene": []
			},
			"id": "scene",
			"mixers": 0,
			"monitoring_type": 0,
			"muted": false,
			"name": "Black",
			"prev_ver": 486539266,
			"private_settings": {},
			"push-to-mute": false,
			"push-to-mute-delay": 0,
			"push-to-talk": false,
			"push-to-talk-delay": 0,
			"settings": {
				"custom_size": false,
				"id_counter": 0,
				"items": []
			},
			"sync": 0,
			"versioned_id": "scene",
			"volume": 1.0
		},
		{
			"balance": 0.5,
			"deinterlace_field_order": 0,
			"deinterlace_mode": 0,
			"enabled": true,
			"flags": 0,
			"hotkeys": {
				"OBSBasic.SelectScene": [],
				"libobs.hide_scene_item.Starting Soon": [],
				"libobs.show_scene_item.Starting Soon": []
			},
			"id": "scene",
			"mixers": 0,
			"monitoring_type": 0,
			"muted": false,
			"name": "PreStart",
			"prev_ver": 486539266,
			"private_settings": {},
			"push-to-mute": false,
			"push-to-mute-delay": 0,
			"push-to-talk": false,
			"push-to-talk-delay": 0,
			"settings": {
				"custom_size": false,
				"id_counter": 1,
				"items": [
					{
						"align": 5,
						"blend_method": "default",
						"blend_type": "normal",
						"bounds": {
							"x": 0.0,
							"y": 0.0
						},
						"bounds_align": 0,
						"bounds_type": 0,
						"crop_bottom": 0,
						"crop_left": 0,
						"crop_right": 0,
						"crop_top": 0,
						"group_item_backup": false,
						"hide_transition": {
							"duration": 0
						},
						"id": 1,
						"locked": false,
						"name": "Starting Soon",
						"pos": {
							"x": 33.0,
							"y": 485.0
						},
						"private_settings": {},
						"rot": 0.0,
						"scale": {
							"x": 1.0,
							"y": 1.0
						},
						"scale_filter": "disable",
						"show_transition": {
							"duration": 0
						},
						"visible": true
					}
				]
			},
			"sync": 0,
			"versioned_id": "scene",
			"volume": 1.0
		},
		{
			"balance": 0.5,
			"deinterlace_field_order": 0,
			"deinterlace_mode": 0,
			"enabled": true,
			"flags": 0,
			"hotkeys": {
				"OBSBasic.SelectScene": [],
				"libobs.hide_scene_item.End message": [],
				"libobs.show_scene_item.End message": []
			},
			"id": "scene",
			"mixers": 0,
			"monitoring_type": 0,
			"muted": false,
			"name": "End",
			"prev_ver": 486539266,
			"private_settings": {},
			"push-to-mute": false,
			"push-to-mute-delay": 0,
			"push-to-talk": false,
			"push-to-talk-delay": 0,
			"settings": {
				"custom_size": false,
				"id_counter": 1,
				"items": [
					{
						"align": 5,
						"blend_method": "default",
						"blend_type": "normal",
						"bounds": {
							"x": 0.0,
							"y": 0.0
						},
						"bounds_align": 0,
						"bounds_type": 0,
						"crop_bottom": 0,
						"crop_left": 0,
						"crop_right": 0,
						"crop_top": 0,
						"group_item_backup": false,
						"hide_transition": {
							"duration": 0
						},
						"id": 1,
						"locked": false,
						"name": "End message",
						"pos": {
							"x": 96.0,
							"y": 474.0
						},
						"private_settings": {},
						"rot": 0.0,
						"scale": {
							"x": 1.0,
							"y": 1.0
						},
						"scale_filter": "disable",
						"show_transition": {
							"duration": 0
						},
						"visible": true
					}
				]
			},
			"sync": 0,
			"versioned_id": "scene",
			"volume": 1.0
		},
		{
			"balance": 0.5,
			"deinterlace_field_order": 0,
			"deinterlace_mode": 0,
			"enabled": true,
			"flags": 0,
			"hotkeys": {},
			"id": "text_ft2_source",
			"mixers": 0,
			"monitoring_type": 0,
			"muted": false,
			"name": "Paused Message",
			"prev_ver": 486539266,
			"private_settings": {},
			"push-to-mute": false,
			"push-to-mute-delay": 0,
			"push-to-talk": false,
			"push-to-talk-delay": 0,
			"settings": {
				"font": {
					"face": "Sans Serif",
					"flags": 0,
					"size": 96,
					"style": ""
				},
				"text": "Paused for the \nadministration\nof the sacrament"
			},
			"sync": 0,
			"versioned_id": "text_ft2_source_v2",
			"volume": 1.0
		},
		{
			"balance": 0.5,
			"deinterlace_field_order": 0,
			"deinterlace_mode": 0,
			"enabled": true,
			"flags": 0,
			"hotkeys": {},
			"id": "text_ft2_source",
			"mixers": 0,
			"monitoring_type": 0,
			"muted": false,
			"name": "Starting Soon",
			"prev_ver": 486539266,
			"private_settings": {},
			"push-to-mute": false,
			"push-to-mute-delay": 0,
			"push-to-talk": false,
			"push-to-talk-delay": 0,
			"settings": {
				"font": {
					"face": "Sans Serif",
					"flags": 0,
					"size": 128,
					"style": ""
				},
				"text": "The broadcast will start soon"
			},
			"sync": 0,
			"versioned_id": "text_ft2_source_v2",
			"volume": 1.0
		},
		{
			"balance": 0.5,
			"deinterlace_field_order": 0,
			"deinterlace_mode": 0,
			"enabled": true,
			"flags": 0,
			"hotkeys": {},
			"id": "text_ft2_source",
			"mixers": 0,
			"monitoring_type": 0,
			"muted": false,
			"name": "End message",
			"prev_ver": 486539266,
			"private_settings": {},
			"push-to-mute": false,
			"push-to-mute-delay": 0,
			"push-to-talk": false,
			"push-to-talk-delay": 0,
			"settings": {
				"font": {
					"face": "Sans Serif",
					"flags": 0,
					"size": 128,
					"style": ""
				},
				"text": "The broadcast has finished"
			},
			"sync": 0,
			"versioned_id": "text_ft2_source_v2",
			"volume": 1.0
		}
	],
	"transition_duration": 300,
	"transitions": [],
	"virtual-camera": {
		"internal": 0,
		"scene": "",
		"source": "",
		"type": 0
	}
}
EOF

# prerequisites for docker
sudo apt install apt-transport-https ca-certificates curl software-properties-common
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /usr/share/keyrings/docker-archive-keyring.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/docker-archive-keyring.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update
sudo apt-cache policy docker-ce
sudo apt install docker-ce


# SSL Certificate setup
sudo curl -o /usr/bin/getssl --silent https://raw.githubusercontent.com/srvrco/getssl/latest/getssl 
sudo chmod 771 /usr/bin/getssl

sudo mkdir -p /opt/getssl/

sudo curl -o /opt/getssl/dns_add_cloudflare --silent https://raw.githubusercontent.com/srvrco/getssl/master/dns_scripts/dns_add_cloudflare
sudo curl -o /opt/getssl/dns_del_cloudflare --silent https://raw.githubusercontent.com/srvrco/getssl/master/dns_scripts/dns_del_cloudflare

sudo chmod 771 /opt/getssl/*

sudo getssl -c "*.$DOMAIN"

sudo sed -i 's/PRIVATE_KEY_ALG="rsa"/PRIVATE_KEY_ALG="prime256v1"/' /root/.getssl/getssl.cfg
sudo sed -i 's/SERVER_TYPE="https"/#SERVER_TYPE="https"/' /root/.getssl/getssl.cfg
sudo sed -i 's/CHECK_REMOTE="true"/CHECK_REMOTE="false"/' /root/.getssl/getssl.cfg

TIMESTAMP=$(date)
TIMESTAMP=${TIMESTAMP// /_}
sudo mv /root/.getssl/\*.$DOMAIN/getssl.cfg /root/.getssl/\*.$DOMAIN/getssl.cfg.$TIMESTAMP

cat << EOF | sudo tee /root/.getssl/\*.$DOMAIN/getssl.cfg > /dev/null
CA="https://acme-v02.api.letsencrypt.org"
VALIDATE_VIA_DNS="true"
DNS_ADD_COMMAND="CF_API_TOKEN=$CF_TOKEN CF_ZONE_ID=$CF_ZONE_ID /opt/getssl/dns_add_cloudflare"
DNS_DEL_COMMAND="CF_API_TOKEN=$CF_TOKEN CF_ZONE_ID=$CF_ZONE_ID /opt/getssl/dns_del_cloudflare"
PUBLIC_DNS_SERVER="1.1.1.1"
AUTH_DNS_SERVER="1.1.1.1"
FULL_CHAIN_INCLUDE_ROOT="true"
DOMAIN_CERT_LOCATION="/etc/ssl/$DOMAIN.crt" 
DOMAIN_KEY_LOCATION="/etc/ssl/$DOMAIN.key"
CA_CERT_LOCATION="/etc/ssl/ca_chain.crt" 
DOMAIN_CHAIN_LOCATION="/etc/ssl/$DOMAIN.chain.crt"
DOMAIN_PEM_LOCATION="/etc/ssl/$DOMAIN.full.pem" 
RELOAD_CMD="openssl pkcs12 -export -nodes -out /etc/ssl/$DOMAIN.pfx -inkey /etc/ssl/$DOMAIN.key -in /etc/ssl/$DOMAIN.chain.crt -passout pass:  ; chmod 640 /etc/ssl/$DOMAIN* ; chown :ssl-cert /etc/ssl/$DOMAIN*"

EOF

#sed -ri 's/^(SANS=")/#\1/' /root/.getssl/\*.$DOMAIN/getssl.cfg

# generate & install the certificates
sudo getssl "*.$DOMAIN"


cat << EOF | sudo tee /etc/cron.d/cert_renewal > /dev/null
# run certificate renewal check daily at 08:03 
03 08 * * * root /usr/bin/getssl -a -q -u 
EOF

# encrypt rtmp traffic to the distribution server
sudo apt -y install stunnel4

sudo mkdir -p /var/log/stunnel4

sudo chown stunnel4:stunnel4 /var/log/stunnel4

cat << EOF | sudo tee /etc/stunnel/rtmp_out.conf > /dev/null
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


# check if the rtmp section of the nginx config exists, add it if not.
if [ $(grep -c "rtmp" /etc/nginx/nginx.conf) -eq 0 ]; then
cat << EOF | sudo tee /etc/nginx/nginx.conf > /dev/null

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

						# push one stream to the OvenMediaEngine
                        push rtmp://127.0.0.1:19352/app/sac1;
						# push another copy to the stunnel port, which gets tunneled to the remote distribution server
                        push rtmp://127.0.0.1:19353/live/sac1;
                }
        }
}
EOF
fi

cat << EOF | sudo tee /etc/nginx/sites-available/BroadcastManager > /dev/null
    server {
        listen 443 ssl;
        ssl_certificate     /etc/ssl/$DOMAIN.chain.crt;
        ssl_certificate_key /etc/ssl/$DOMAIN.key;

        location / {
            proxy_pass http://127.0.0.1:5000;
        }
    }

server {
        listen 80 default_server;
        listen [::]:80 default_server;

        server_name _;
		
		return 301 https://\$host\$request_uri;

        root /var/www/html;
}
EOF


cat << EOF | sudo tee /etc/nginx/sites-available/rtmp-stats > /dev/null
server {
    listen 8080;
    server_name  _;

    # rtmp stat
    location /stat {
        rtmp_stat all;
        rtmp_stat_stylesheet stat.xsl;
    }
    location /stat.xsl {
        root /var/www/html/rtmp;
    }

    # rtmp control
    location /control {
        rtmp_control all;
    }
}

server {
    listen 8088;
    add_header Access-Control-Allow-Origin *;

    location / {
        root /var/www/html/stream;
    }
}

types {
    application/dash+xml mpd;
}
EOF

sudo mkdir -p /var/www/html/rtmp
sudo chown www-data /var/www/html/rtmp
sudo cp /usr/share/doc/libnginx-mod-rtmp/examples/stat.xsl /var/www/html/rtmp/

sudo rm /etc/nginx/sites-enabled/default
sudo ln -s /etc/nginx/sites-available/BroadcastManager /etc/nginx/sites-enabled/BroadcastManager
sudo ln -s /etc/nginx/sites-available/rtmp-stats /etc/nginx/sites-enabled/rtmp-stats


sudo systemctl restart nginx


# OvenMediaEngine configuration
cat << EOF | sudo tee /tmp/ometls.txt > /dev/null

                                <TLS>
                                        <CertPath>/etc/ssl/$DOMAIN.chain.crt</CertPath>
                                        <KeyPath>/etc/ssl/$DOMAIN.key</KeyPath>
                                </TLS>
EOF

sudo mkdir -p /opt/ome

sudo perl -i -p0e 's/\s+<!--\n\s+<TLS>.*?<\/TLS>\n\s+-->\n/`cat \/tmp\/ometls.txt`/se' /opt/ome/origin.conf.xml


sudo docker run --restart=always --name ome -d -e OME_HOST_IP=test.$DOMAIN \
-p 19352:1935 -p 9999:9999/udp -p 9000:9000 -p 3333:3333 -p 3334:3334 -p 3478:3478 -p 10000-10009:10000-10009/udp \
-v /etc/ssl/$DOMAIN.chain.crt:/etc/ssl/$DOMAIN.chain.crt \
-v /etc/ssl/$DOMAIN.key:/etc/ssl/$DOMAIN.key \
-v /opt/ome/origin.conf.xml:/opt/ovenmediaengine/bin/origin_conf/Server.xml \
airensoft/ovenmediaengine:latest

#-v /opt/ovenmediaengine/edge.conf.xml:/opt/ovenmediaengine/bin/edge_conf/Server.xml \


# create the autostart directory
mkdir -p ~/.config/autostart/

# lock the desktop on login
cat << EOF > ~/.config/autostart/gnome-screensaver-command.desktop
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

# prevent the user from being able to log out. Desktop has to stay running for OBS to be able to start.
cat << EOF | sudo tee /etc/dconf/profile/user/opt/ome
user-db:user
system-db:local
EOF

sudo mkdir -p /etc/dconf/db/local.d/

cat << EOF | sudo tee /etc/dconf/db/local.d/00-logout > /dev/null
[org/gnome/desktop/lockdown]
# Prevent the user from logging out
disable-log-out=true

# Prevent the user from user switching
disable-user-switching=true
EOF

cat << EOF | sudo tee /etc/dconf/db/local.d/locks/lockdown > /dev/null
# disable user logout
/org/gnome/desktop/lockdown/disable-log-out

# disable user switching
/org/gnome/desktop/lockdown/disable-user-switching
EOF

sudo dconf update


# enable autologin for user (thus starting OBS, which cannot run headless at this time)
sudo sed -ri "s/^\s*#\s*AutomaticLoginEnable\s*=.*/AutomaticLoginEnable = true/" /etc/gdm3/custom.conf
sudo sed -ri "s/^\s*#\s*AutomaticLogin\s*=.*/AutomaticLogin = $OBS_USER/" /etc/gdm3/custom.conf

# make it possible to use VNC - disable wayland 
sudo sed -ri "s/^\s*#\s*WaylandEnable\s*=.*/WaylandEnable=false/" /etc/gdm3/custom.conf

# Configure VNC server for remote configuration
sudo apt install -y xfce4 xfce4-goodies
sudo apt install -y tightvncserver expect


PROG=$(/usr/bin/which vncpasswd) 
/usr/bin/expect <<EOF
spawn "$PROG"
expect "Password:"
send "$VNC_PASSWORD\r"
expect "Verify:"
send "$NEWPASS\r"
expect "Would you like to enter a view-only password (y/n)?"
send "\r"
expect eof
exit
EOF

vncserver &
# should put in an actual test here...
sleep 5
vncserver -kill :1
mv ~/.vnc/xstartup ~/.vnc/xstartup.bak

cat << EOF > ~/.vnc/xstartup
#!/bin/bash
xrdb $HOME/.Xresources
startxfce4 &
EOF

chmod +x ~/.vnc/xstartup

# DO THIS IN CODE ON START
cat << EOF | sudo tee /opt/start-obs.sh > /dev/null
#!/usr/bin/bash
if ! pgrep -x obs >/dev/null
then
echo no obs!
DISPLAY=:0 sudo --preserve-env=DISPLAY -u $OBS_USER obs &
fi
EOF

sudo chown $USER /opt/start-obs.sh
chmod 774 /opt/start-obs.sh


# create a user account for running the Broadcast Manager app
adduser --system --quiet --no-create-home --disabled-password --disabled-login --gecos "" broad-man

# grant the broadcast manager app access to ssl key files
usermod -a -G ssl-cert broad-man

# pull the latest BroadcastManager.zip package
curl -L -o /tmp/BroadcastManager.zip https://github.com/hinmanjp/BroadcastManager/releases/latest/download/BroadcastManager.zip
sudo unzip -o -d /opt/BroadcastManager /tmp/BroadcastManager.zip
sudo chown -R broad-man /opt/BroadcastManager
chmod 700 /opt/BroadcastManager/BroadcastManager2


# configure BroadcastManager as a service
cat << EOF | sudo tee /etc/systemd/system/BroadcastManager.service > /dev/null
[Unit]
Description=ASP.NET Core Church Broadcast Management UI

[Service]
# will set the Current Working Directory (CWD)
WorkingDirectory=/opt/BroadcastManager
# systemd will run this executable to start the service
# if /usr/bin/dotnet doesn't work, use `which dotnet` to find correct dotnet executable path
ExecStart=/opt/BroadcastManager/BroadcastManager2
# to query logs using journalctl, set a logical name here
SyslogIdentifier=BroadcastManager

# Use your username to keep things simple, for production scenario's I recommend a dedicated user/group.
# If you pick a different user, make sure dotnet and all permissions are set correctly to run the app.
# To update permissions, use 'chown yourusername -R /srv/AspNetSite' to take ownership of the folder and files,
#       Use 'chmod +x /srv/AspNetSite/AspNetSite' to allow execution of the executable file.
User=broad-man

# ensure the service restarts after crashing
Restart=always
# amount of time to wait before restarting the service
RestartSec=5

# copied from dotnet documentation at
# https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx
KillSignal=SIGINT
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=ASPNETCORE_URLS=http://*:5000

AmbientCapabilities=CAP_NET_BIND_SERVICE

[Install]
WantedBy=multi-user.target
EOF

# need to configure minimum appsettings.json if one doesn't already exist
# should check for the existance of a value for each settings if appsettings already exists
# ideally, should only ask for input of values for settings that don't already exist
if [ ! -f /opt/BroadcastManager/appsettings.json ]
  sudo -u broad-man cp /opt/BroadcastManager/appsettings.json.sample /opt/BroadcastManager/appsettings.json
  sudo sed -ri 's/("VultrApiKey": )""/\\1"$VULTR_TOKEN"/' /opt/BroadcastManger/appsettings.json
  sudo sed -ri 's/("ObsApiKey": )""/\\1"$OBS_APIKEY"/' /opt/BroadcastManger/appsettings.json
  sudo sed -ri 's/("CloudFlareTokenKey": )""/\\1"$CF_TOKEN"/' /opt/BroadcastManger/appsettings.json
  sudo sed -ri 's/("DomainName": )""/\\1"$DOMAIN"/' /opt/BroadcastManger/appsettings.json
  sudo sed -ri 's/("AppMasterPassword": )""/\\1"$APP_MASTER_PW"/' /opt/BroadcastManger/appsettings.json
  sudo sed -ri 's/("SshPrivateKeyFile": )""/\\1"$SSH_PRIVATE_KEY_PATH"/' /opt/BroadcastManger/appsettings.json
  sudo sed -ri 's/("SshPublicKeyFile": )""/\\1"$SSH_PRIVATE_KEY_PATH.pub"/' /opt/BroadcastManger/appsettings.json
  sudo sed -ri 's/("TimeZone": )""/\\1"$TIMEZONE"/' /opt/BroadcastManger/appsettings.json
fi

sudo systemctl daemon-reload
sudo systemctl restart BroadcastManager


# manual step - if desired
# Configure a VPN client to allow for remote configuration & management




