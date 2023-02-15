
# Installing required packages
sudo apt-get install -y python3-pip
sudo apt-get install -y hostapd
sudo apt-get install -y dnsmasq

# Installing python libs for root (used by systemd)
sudo pip3 install pymodbus
sudo pip3 install pyserial

# Installing python libs for pi user (if you need to launch server manually from the shell)
pip3 install pymodbus
pip3 install pyserial

# Copying config files for hostapd (hotspot wifi) and dnsmasq (dhcp server wlan side)
sudo mv /home/pi/hostapd.conf /etc/hostapd/hostapd.conf
sudo mv /home/pi/dnsmasq.conf /etc/dnsmasq.conf
sudo mv /home/pi/wlan0 /etc/network/interfaces.d/wlan0

# Copying python server in home directory
chown pi /home/pi/ModBusServer/config.json

# Creating symlink for systemd service file
sudo rm /etc/systemd/system/modbus.service
sudo ln -s /home/pi/ModBusServer/modbus.service /lib/systemd/system/modbus.service
sudo systemctl daemon-reload
sudo systemctl enable modbus.service
sudo service modbus start


# Sometimes the hostapd service is masked after the installation so i solve this checking the actual status
if sudo service hostapd status | grep "Loaded: masked" -q;
then
        echo "Hostapd status masked, it needs to be unmasked, ..."
        sudo systemctl unmask hostapd.service
        sudo systemctl enable hostapd.service
        #sudo service hostapd status
        sudo service hostapd status | grep "Loaded: masked"
        sudo service hostapd start
        #sudo service hostapd status
        sudo service hostapd status | grep "Loaded: masked"

        if sudo service hostapd status | grep "Loaded: masked" -q;
        then
                echo "Error running unmask command on hostapd.service"
        else
                echo "Hostapd status OK"
        fi
else
        echo "Hostapd status OK"
fi

# Now i configure wlan0 manually
sudo ifconfig wlan0 10.0.0.1 netmask 255.255.255.0

# But i also add the config entry in th network file