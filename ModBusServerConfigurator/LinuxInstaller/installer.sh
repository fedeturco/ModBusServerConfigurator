
PATH_GIT_PROJECT="https://github.com/Fedex1515/ModBusServerConfigurator/settings"

sudo su

apt-get install -y python3-pip
apt-get install -y git
apt-get install -y hostapd
apt-get install -y dnsmasq

pip3 install pymodbus
pip3 install pyserial

cp ModBusServerConfigurator/LinuxInstaller/hostapd.conf /etc/hostapd/hostapd.conf
cp ModBusServerConfigurator/LinuxInstaller/dnsmasq.conf /etc/dnsmasq.conf

cp ModBusServerConfigurator/PythonEngine /home/pi/ModBusServerConfigurator

chown pi /home/pi/ModBusServerConfigurator/config.json

ln -s 

git clone $PATH_GIT_PROJECT

exit

pip3 install pymodbus
pip3 install pyserial