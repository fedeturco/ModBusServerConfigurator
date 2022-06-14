
sudo su

PATH_GIT_PROJECT="https://github.com/Fedex1515/ModBusServerConfigurator/settings"

apt-get install -y python3-pip
apt-get install -y git
apt-get install -y hostapd
apt-get install -y dnsmasq

pip3 install pymodbus
pip3 install pyserial

git clone $PATH_GIT_PROJECT

cp ModBusServerConfigurator/LinuxInstaller/hostapd.conf /etc/hostapd/hostapd.conf
cp ModBusServerConfigurator/LinuxInstaller/dnsmasq.conf /etc/dnsmasq.conf

cp ModBusServerConfigurator/PythonEngine /home/pi/ModBusServerConfigurator

chown pi /home/pi/ModBusServerConfigurator/config.json

ln -s /home/pi/ModBusServerConfigurator/modbus.service /etc/systemd/system/modbus.service
systemctl enable modbus.service

exit

pip3 install pymodbus
pip3 install pyserial