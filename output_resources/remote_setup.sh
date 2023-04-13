#!/usr/bin/env bash

# make sure the server is running in the timezone we expect
timedatectl set-timezone {{TIMEZONE}}

groupadd -f ssl-cert

chmod 440 {{SSL_KEY_PATH}} ; chown :ssl-cert {{SSL_KEY_PATH}}
chmod 440 {{SSL_PFX_PATH}} ; chown :ssl-cert {{SSL_PFX_PATH}}

# allow the web user to be able to read ssl keys
usermod -a -G ssl-cert www-data

if [ $(grep -c "{{DOMAIN}}" /etc/hosts) -eq 0 ]; then
cat << EOF >> /etc/hosts
127.0.0.1    localhost.{{DOMAIN}}
EOF
fi

###############################################################
#  START INSTALL OF DOTNET RUNTIME                            #
# Tell ubuntu to prefer Microsoft's .net packages over it's own
#if [ ! -f /etc/apt/preferences ] || [ $(grep -c "Package: dotnet\* aspnet\* netstandard\*" /etc/apt/preferences) -eq 0 ]; then
cat << EOF > /etc/apt/preferences.d/dotnet
Package: dotnet* aspnet* netstandard*
Pin: origin "archive.ubuntu.com"
Pin-Priority: -10
EOF
#fi

# Get Ubuntu version
declare repo_version=$(if command -v lsb_release &> /dev/null; then lsb_release -r -s; else grep -oP '(?<=^VERSION_ID=).+' /etc/os-release | tr -d '"'; fi)

# Download Microsoft signing key and repository
wget https://packages.microsoft.com/config/ubuntu/$repo_version/packages-microsoft-prod.deb -O packages-microsoft-prod.deb

# Install Microsoft signing key and repository
dpkg -i packages-microsoft-prod.deb

# Clean up
rm packages-microsoft-prod.deb

# Update packages
apt update

# install nginx, the rtmp mod, and .net core
apt install -y libnginx-mod-rtmp aspnetcore-runtime-7.0
##############################################################

# extract the broadcast auth web api service
unzip -o -d /opt/broadcastAuth /tmp/broadcastAuth.zip

chown www-data:www-data /opt/broadcastAuth

# set the broadcast auth settings as we need them in production

if [ $(grep -c "{{SSL_PFX_PATH}}" /opt/broadcastAuth/appsettings.json) -eq 0 ]; then
cat << EOF >>  /opt/broadcastAuth/appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AuthFile": "AuthList.json",
  "Kestrel": {
    "Certificates": {
      "Default": {
        "Path": "{{SSL_PFX_PATH}}",
        "Password": ""
      }
    },
    "Endpoints": { "Https": { "Url": "https://0.0.0.0:2884" } }
  }
}
EOF
fi


# open the port to the authorizer
# ufw allow 2884/tcp

# open the port for the rtmp stream. The nginx rtmp library doesn't support ssl/tls right now, so, security by obscurity :(
ufw allow 1936/tcp

ufw allow 443/tcp
ufw allow 80/tcp

# make a directory for the RTMP Stats
mkdir -p /var/www/html/rtmp

# make a directory for the hls & dash broadcast files are stored
mkdir -p /var/www/html/stream/{hls,dash}

# directory for javascript files
mkdir -p /var/www/html/scripts
mv /tmp/js_scripts/* /var/www/html/scripts

# make sure the stream paths can be accessed
chmod 755 /var/www/html/stream/{hls,dash}

chown -R www-data /var/www/html/stream

# if the ssl section isn't already configured, add it
if [ $(grep -c "{{ESCAPED_CERT_PATH}}" /etc/nginx/nginx.conf) -eq 0 ]; then
sed -ri 's/(^\s+ssl_prefer_server_ciphers on;)/\1\n        ssl_certificate     {{ESCAPED_CERT_PATH}};\n        ssl_certificate_key {{ESCAPED_KEY_PATH}};/' /etc/nginx/nginx.conf
fi

# add rtmp config to nginx if it doesn't already exist
if [ $(grep -c "rtmp" /etc/nginx/nginx.conf) -eq 0 ]; then
cat << EOF >>  /etc/nginx/nginx.conf

stream {

    upstream backend {
        server 127.0.0.1:1935;
    }

    server {
        listen 1936 ssl;
        proxy_pass backend;
        proxy_protocol on;
        ssl_certificate     {{SSL_CERT_PATH}};
        ssl_certificate_key {{SSL_KEY_PATH}};
    }
}

rtmp {
        server {
                listen 127.0.0.1:1935 proxy_protocol;
                chunk_size 4000;
                allow publish all;
                #deny publish all;
                deny play all;

                application live {
                        live on;
                        record off;
                        # disable auth until I figure out what nginx is passing to the auth service
                        #on_publish http://127.0.0.1:2884/rtmp-auth/;

                        hls on;
                        hls_path /var/www/html/stream/hls;
                        hls_fragment 3;
                        hls_playlist_length 120m;
                        hls_continuous on;
                        hls_cleanup off;

                        dash on;
                        dash_path /var/www/html/stream/dash;
                }
        }
}
EOF
fi


# expose the rtmp stats & public site 
cat << EOF > /etc/nginx/sites-available/rtmp
server {
    listen 8080;
#    server_name  localhost;

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
    listen 80 default_server;
    #listen [::]:80 default_server;
    #server_name _;

    return 301 https://\$host\$request_uri;
}

server {
    listen 443 ssl;
    index index.html;
    #add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;

    location / {
        root /var/www/html;
        add_header Access-Control-Allow-Origin *;
        autoindex on;

        location /stream {
            add_header Access-Control-Allow-Origin *;
            #add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
#            root /var/www/html/stream;
        }
    }
}
types {
    application/dash+xml mpd;
}
EOF

# enable our streaming site
ln -s /etc/nginx/sites-available/rtmp /etc/nginx/sites-enabled/rtmp

# disable the default site
rm /etc/nginx/sites-enabled/default

# copy required file for rtmp stats into place
cp /usr/share/doc/libnginx-mod-rtmp/examples/stat.xsl /var/www/html/rtmp/stat.xsl


cat << EOF > /var/www/html/index.html
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="UTF-8">
    <meta http-equiv="X-UA-Compatible" content="IE=edge">
<!--     <meta name="viewport" content="width=device-width, initial-scale=1.0"> -->
    <title>Idaho Falls North Stake Broadcast</title>
</head>

<body>
<script src="https://cdn.jsdelivr.net/npm/hls.js@1"></script>
<video id="video" controls autoplay style="width: 100%; height: auto"></video>
<script>
  var video = document.getElementById('video');
  var videoSrc = '/stream/hls/sac1.m3u8';
  if (Hls.isSupported()) {
    var hls = new Hls();
    hls.loadSource(videoSrc);
    hls.attachMedia(video);
  }
  else if (video.canPlayType('application/vnd.apple.mpegurl')) {
    video.src = videoSrc;
  }
</script>

</body>

</html>
EOF

systemctl restart nginx

cat << EOF > /var/www/html/alive.html
<!DOCTYPE html>
<html>
<body>
I am alive!
</body>
</html>
EOF


cat << EOF > /tmp/hls_check.sh
#!/usr/bin/env bash

while  [ \$(ls /var/www/html/stream/hls/ | wc -l) -lt 2 ]
do
  sleep 0.3
done
echo hsl is running

EOF

chmod +x /tmp/hls_check.sh


cat << EOF > /etc/cron.d/remove_vm
# Remove the DNS Record for the distribution server just before self destruction
20 25 * * * root curl curl --request DELETE  --url 'https://api.cloudflare.com/client/v4/zones/{{DNS_ZONE_ID}}/dns_records/{{HOST_RECORD_ID}}' --header 'Content-Type: application/json' --header 'X-Auth-Key: {{CF_APIKEY}} '
# Schedule the machine to self destruct at midnight
0 0 * * * root curl --location --request DELETE 'https://api.vultr.com/v2/instances/{{VM_INSTANCE_ID}}' --header 'Authorization: Bearer {{VULTR_API_KEY}}'

EOF


