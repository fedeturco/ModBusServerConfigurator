# ModBusServerConfigurator


## Release

### v1.6
https://github.com/fedeturco/ModBusServerConfigurator/releases/download/1.6/ModBusServerConfigurator_v1.6.zip

A partire dalla versione 1.4 l'applicativo nella versione su raspberry è stato spostato da /home/pi/ModBusServer a /etc/ModBusServer.

## Installer raspberry

https://github.com/fedeturco/ModBusServerConfigurator/blob/master/ModBusServerConfigurator/LinuxInstaller/installer.sh

A partire dalla versione 1.1 l'installer è integrato direttamente nel client, viene richiesto all'utente di effettuare l'installazione qual ora non il client trovi il damone installato sul target configurato (raspberry o altra board linux embedded).

## Descrizione

ModBusServerConfigurator Si tratta di un client configurazione server ModBus per simulare slave multipli sia TCP che RTU. Il client può essere usato sia standalone su Windows che come interfaccia di configurazione per un server in esecuzione su un Raspberry o altra board linux embedded. Per creare una nuova head aggiungere una nuova riga alla tabella e trascinare (drag & drop) i profili che si vogliono abilitare sulla head desiderata.

![alt text](https://github.com/fedeturco/ModBusServerConfigurator/blob/master/ModBusServerConfigurator/Img/Screenshot_1.PNG?raw=true)

Usato standalone su Windows è possibile configurare slave multipli, premendo start viene avviato il server python e l'output mostrato nella console seguente. Su windows è possibile fare il bind del server TCP solo sugli IP delle interfacce disponibili, mentre su Raspberry è possibile utilizzare qualsiasi IP, sarà poi il server all'avvio a settare ip multipli sulla eth0 qual ora l'Ip da simulare non sia già presente in una delle interfacce del Raspberry.

![alt text](https://github.com/fedeturco/ModBusServerConfigurator/blob/master/ModBusServerConfigurator/Img/Screenshot_2.PNG?raw=true)

Nella tab modbus mapping è possibile creare nuovi profili o modificare i profili esistenti. Attenzione che quando si inseriscono dei registri questi devono essere contenuti nella Len del buffer (eventualmente aumentare la Len fino all'ultimo registro desiderato). i profili possono anche essere importati ed esportati fra diverse copie del client.

![alt text](https://github.com/fedeturco/ModBusServerConfigurator/blob/master/ModBusServerConfigurator/Img/Screenshot_3.PNG?raw=true)

Nell'ultima tab settings sono presenti le varie impostazioni del client così come la configurazione di rete per raggiungere il Raspberry desiderato.

![alt text](https://github.com/fedeturco/ModBusServerConfigurator/blob/master/ModBusServerConfigurator/Img/Screenshot_4.PNG?raw=true)
