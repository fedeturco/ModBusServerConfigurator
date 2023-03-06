#!/usr/bin/env python3

# Copyright (c) 2021 Federico Turco

# Permission is hereby granted, free of charge, to any person
# obtaining a copy of this software and associated documentation
# files (the "Software"), to deal in the Software without
# restriction, including without limitation the rights to use,
# copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the
# Software is furnished to do so, subject to the following
# conditions:

# The above copyright notice and this permission notice shall be
# included in all copies or substantial portions of the Software.

# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
# EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
# OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
# NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
# HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
# OTHER DEALINGS IN THE SOFTWARE.

# Version 0.3

import atexit
import signal
import os

from sys import platform

try:
    import serial
    import serial.tools.list_ports
except:
    try:
        print(os.popen("pip3 install pyserial").read())

        # Su linux installo libreria anche per root
        if(platform == "linux" or platform == "linux2"):
            print(os.popen("sudo pip3 install pyserial").read())

        import serial
        import serial.tools.list_ports
    except:
        print("")
        print("Error installing pyserial, check internet connection")
        exit()

try:
    from pymodbus.version import version
    from pymodbus.server import StartTcpServer
    from pymodbus.server import StartSerialServer

    from pymodbus.device import ModbusDeviceIdentification
    from pymodbus.datastore import ModbusSequentialDataBlock    #, ModbusSparseDataBlock
    from pymodbus.datastore import ModbusSlaveContext, ModbusServerContext
    from pymodbus.transaction import ModbusRtuFramer    #, ModbusBinaryFramer
except:
    print("pymodbus not found")
    print("Installing library pymodbus")
    print("")

    try:
        print(os.popen("pip3 install pymodbus").read())

        # Su linux installo libreria anche per root
        if(platform == "linux" or platform == "linux2"):
            print(os.popen("sudo pip3 install pymodbus").read())

        from pymodbus.version import version
        from pymodbus.server import StartTcpServer
        #from pymodbus.server import StartTlsServer
        #from pymodbus.server import StartUdpServer
        from pymodbus.server import StartSerialServer

        from pymodbus.device import ModbusDeviceIdentification
        from pymodbus.datastore import ModbusSequentialDataBlock    #, ModbusSparseDataBlock
        from pymodbus.datastore import ModbusSlaveContext, ModbusServerContext
        from pymodbus.transaction import ModbusRtuFramer    #, ModbusBinaryFramer
    except:
        print("")
        print("Error installing pymodbus")
        exit()


import logging
FORMAT = ('%(asctime)-15s %(threadName)-15s'
          ' %(levelname)-8s %(module)-15s:%(lineno)-8s %(message)s')
logging.basicConfig(format=FORMAT)
log = logging.getLogger()


import json
import time
import serial

# Si e' passato dai thread a multiprocess perche' migliori nella gestione dei pid e processi (istanzia un processo per ogni oggetto invece che un thread)
from multiprocessing import Process




# ----------------------------------------------------------------------------------------------------
# ---------- Carico configurazione -------------------------------------------------------------------
# ----------------------------------------------------------------------------------------------------

offset = 1

config = json.loads(open("config.json", "r").read())

actualConfig = ""

modbusHeadList = {}
modbusHeadList["TCP"] = []
modbusHeadList["RTU"] = []

# Metto tutti i profili che trovo qua in modo da accederci comodamente con la chiave
profiles = {}


if("modBus" in config):

    print("")
    print("-------------------------------------------------------------------------------")
    print("------------------------------ Simulatore ModBus ------------------------------")
    print("-------------------------------------------------------------------------------")
    print("")
    print("Chiccosoft inc.")
    print("")

    print("Serial port list:")
    ports = serial.tools.list_ports.comports()

    for port, desc, hwid in sorted(ports):
        print("{}: {} [{}]".format(port, desc, hwid))

    if("debug" in config["modBus"]):
        if(config["modBus"]["debug"]):
            log.setLevel(logging.DEBUG)
        else:
            log.setLevel(logging.INFO)
    else:
        log.setLevel(logging.INFO)

    # Carico profili del file
    for profile in config["modBus"]["profiles"]:
        profiles[profile["label"]] = profile

    # -------------------------------------------------------------
    # --------- ModBus TCP ----------------------------------------
    # -------------------------------------------------------------

    # Linux section
    if(platform == "linux" or platform == "linux2"):
        ifconfig = os.popen("ifconfig").read()
        counterEth0 = 100
    
    # Windows section
    else:
        ifconfig = os.popen("ipconfig").read()
        counterEth0 = -1

    for modbusHead in config["modBus"]["TCP"]:

        modbusOBJ = {}

        if(not modbusHead["enable"]):
            modbusOBJ = None
        
        else:
            # Carico impostazioni globali registri
            modbusOBJ = {}
            modbusOBJ["label"] = modbusHead["label"]
            modbusOBJ["ip_address"] = modbusHead["ip_address"]
            modbusOBJ["port"] = modbusHead["port"]
            modbusOBJ["slaves"] = []

            print("")
            print("-----------------------------------------------")
            print("--------------- ModBus TCP HEAD ---------------")
            print("-----------------------------------------------")
            print("")
            print("HEAD: " + modbusHead["label"])
            print("")
            print("IP address: " + modbusHead["ip_address"])
            print("port: " + str(modbusHead["port"]))
            print("")

            # Linux section
            if(platform == "linux" or platform == "linux2"):
                
                # Se non trovo l'indirizzo ip nella scheda ddi rete lo istanzio
                if(ifconfig.find(modbusOBJ["ip_address"]) == -1):
                    if("assignIp" in config["modBus"]):
                        if(config["modBus"]["assignIp"]):
                            
                            # Se l'eth0:xx esiste gia' passo al prossimo e ritento
                            while(ifconfig.find("eth0:" + str(counterEth0)) != -1):
                                counterEth0 += 1

                            os.system("sudo ifconfig eth0:" + str(counterEth0) + " " + modbusOBJ["ip_address"] + " netmask 255.255.255.0")

                            print("Adding address to eth0:")
                            print("sudo ifconfig eth0:" + str(counterEth0) + " " + modbusOBJ["ip_address"] + " netmask 255.255.255.0")
                            counterEth0 += 1

                        # Se IP invalido e assignIp false raise Error
                        else:
                            print("Invalid IP Address")
                            continue
                    
                    # Se IP invalido e chiave non presente nel json raise Error
                    else:
                        print("Invalid IP Address")
                        continue

            # Windows section
            else:
                # Se non trovo l'indirizzo ip nella scheda ddi rete lo istanzio
                if(ifconfig.find(modbusOBJ["ip_address"]) == -1):
                    print("Invalid IP Address")
                    continue


            actualConfig += "\n"
            actualConfig += ("-----------------------------------------------\n")
            actualConfig += ("--------------- ModBus TCP HEAD ---------------\n")
            actualConfig += ("-----------------------------------------------\n")
            actualConfig += "\n"
            actualConfig += ("HEAD: " + modbusHead["label"])
            actualConfig += "\n"
            actualConfig += ("IP address: " + modbusHead["ip_address"])
            actualConfig += "\n"
            actualConfig += ("port: " + str(modbusHead["port"]))
            actualConfig += "\n"

            for profile in modbusHead["slaves"]:
                for slave_ids in profiles[profile]["slave_id"]:

                    currTcp = {}
                    currTcp["slave_id"] = slave_ids

                    print("")
                    print("ModBus TCP slave [" + profile + "]")
                    print("")
                    print("Slave ID: " + str(currTcp["slave_id"]))
                    print("")
                
                    actualConfig += "\n"
                    actualConfig += "ModBus TCP slave [" + profile + "]"
                    actualConfig += "\n"
                    actualConfig += "\n"
                    actualConfig += "Slave ID: " + str(currTcp["slave_id"])
                    actualConfig += "\n"
                    actualConfig += "\n"


                    # Carico oggetti inputs
                    currTcp["list_di"] = [0] * profiles[profile]["di"]["len"]

                    for item in profiles[profile]["di"]["data"]:

                        if("label" in item):
                            print("Digital input: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += ("Digital input: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += "\n"
                        else:
                            print("Digital input: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += ("Digital input: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += "\n"

                        currTcp["list_di"][int(item["reg"]) + offset] = item["value"]

                    # Carico oggetti coils
                    currTcp["list_co"] = [0] * profiles[profile]["co"]["len"]

                    for item in profiles[profile]["co"]["data"]:

                        if("label" in item):
                            print("Coil: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += ("Coil: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += "\n"
                        else:
                            print("Coil: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += ("Coil: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += "\n"

                        currTcp["list_co"][int(item["reg"]) + offset] = item["value"]

                    # Carico oggetti holdings
                    currTcp["list_hr"] = [0] * profiles[profile]["hr"]["len"]

                    for item in profiles[profile]["hr"]["data"]:
                    
                        if("label" in item):
                            print("Holding reg: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += ("Holding reg: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += "\n"
                        else:
                            print("Holding reg: %6s      value: %5s" % (item, item["value"]))
                            actualConfig += ("Holding reg: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += "\n"
                        
                        currTcp["list_hr"][int(item["reg"]) + offset] = item["value"]

                    # Carico oggetti input regs
                    currTcp["list_ir"] = [0] * profiles[profile]["ir"]["len"]

                    for item in profiles[profile]["ir"]["data"]:
                    
                        if("label" in item):
                            print("Input reg: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += ("Input reg: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += "\n"
                        else:
                            print("Input reg: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += ("Input reg: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += "\n"

                        currTcp["list_ir"][int(item["reg"]) + offset] = item["value"]

                    modbusOBJ["slaves"].append(currTcp) 

                modbusHeadList["TCP"].append(modbusOBJ)

        # debug
        #print(currTcp)

    # -------------------------------------------------------------
    # --------- ModBus RTU ----------------------------------------
    # -------------------------------------------------------------

    for modbusHead in config["modBus"]["RTU"]:

        if(not modbusHead["enable"]):
            modbusOBJ = None
        
        else:
            # Carico impostazioni globali registri
            modbusOBJ = {}
            modbusOBJ["label"] = modbusHead["label"]
            modbusOBJ["serial"] = modbusHead["serial"]
            modbusOBJ["baudrate"] = modbusHead["baudrate"]
            modbusOBJ["config"] = modbusHead["config"]
            modbusOBJ["slaves"] = []

            print("")
            print("-----------------------------------------------")
            print("--------------- ModBus RTU HEAD ---------------")
            print("-----------------------------------------------")
            print("")
            print("HEAD: " + modbusHead["label"])
            print("")
            print("port: " + modbusOBJ["serial"])
            print("baudrate: " + str(modbusOBJ["baudrate"]))
            print("config: " + modbusOBJ["config"])
            print("")

            actualConfig += "\n"
            actualConfig += ("-----------------------------------------------\n")
            actualConfig += ("--------------- ModBus RTU HEAD ---------------\n")
            actualConfig += ("-----------------------------------------------\n")
            actualConfig += "\n"
            actualConfig += ("HEAD: " + modbusHead["label"])
            actualConfig += "\n"
            actualConfig += ("port: " + modbusOBJ["serial"])
            actualConfig += "\n"
            actualConfig += ("baudrate: " + str(modbusOBJ["baudrate"]))
            actualConfig += "\n"
            actualConfig += ("config: " + modbusOBJ["config"])
            actualConfig += "\n"

            for profile in modbusHead["slaves"]:
                for slave_ids in profiles[profile]["slave_id"]:

                    currRtu = {}
                    currRtu["slave_id"] = slave_ids

                    print("")
                    print("ModBus RTU slave [" + profile + "]")
                    print("")
                    print("Slave ID: " + str(currRtu["slave_id"]))
                    print("")

                    actualConfig += "\n"
                    actualConfig += ("ModBus RTU slave [" + profile + "]")
                    actualConfig += "\n"
                    actualConfig += "\n"
                    actualConfig += ("Slave ID: " + str(currRtu["slave_id"]))
                    actualConfig += "\n"
                    actualConfig += "\n"


                    # Carico oggetti inputs
                    currRtu["list_di"] = [0] * profiles[profile]["di"]["len"]

                    for item in profiles[profile]["di"]["data"]:

                        if("label" in item):
                            print("Digital input: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += ("Digital input: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += "\n"
                        else:
                            print("Digital input: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += ("Digital input: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += "\n"

                        currRtu["list_di"][int(item["reg"]) + offset] = item["value"]

                    # Carico oggetti coils
                    currRtu["list_co"] = [0] * profiles[profile]["co"]["len"]

                    for item in profiles[profile]["co"]["data"]:

                        if("label" in item):
                            print("Coil: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += ("Coil: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += "\n"
                        else:
                            print("Coil: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += ("Coil: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += "\n"

                        currRtu["list_co"][int(item["reg"]) + offset] = item["value"]

                    # Carico oggetti holdings
                    currRtu["list_hr"] = [0] * profiles[profile]["hr"]["len"]

                    for item in profiles[profile]["hr"]["data"]:
                    
                        if("label" in item):
                            print("Holding reg: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += ("Holding reg: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += "\n"
                        else:
                            print("Holding reg: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += ("Holding reg: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += "\n"
                        
                        currRtu["list_hr"][int(item["reg"]) + offset] = item["value"]

                    # Carico oggetti input regs
                    currRtu["list_ir"] = [0] * profiles[profile]["ir"]["len"]

                    for item in profiles[profile]["ir"]["data"]:
                    
                        if("label" in item):
                            print("Input reg: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += ("Input reg: %6s      value: %5s      label: %s" % (item["reg"], item["value"], item["label"]))
                            actualConfig += "\n"
                        else:
                            print("Input reg: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += ("Input reg: %6s      value: %5s" % (item["reg"], item["value"]))
                            actualConfig += "\n"

                        currRtu["list_ir"][int(item["reg"]) + offset] = item["value"]

                    modbusOBJ["slaves"].append(currRtu) 

            modbusHeadList["RTU"].append(modbusOBJ)

# ----------------------------------------------------------------------------------------------------

print("\n")
print("-----------------------------------------------")
print("--------------- STARTING SERVER ---------------")
print("-----------------------------------------------")

# debug
#file_ = open("ram_object.json", "w")
#file_.write(json.dumps(modbusHeadList))
#file_.close()

file_ = open("actual_config.txt", "w")
file_.write(actualConfig)
file_.close()

# -------------------------------------------------------------
# --------- ModBus TCP ----------------------------------------
# -------------------------------------------------------------

def runServerTcp(name, objectTcp):

    time.sleep(0.5 + objectTcp["delay"] * 0.5)

    if(objectTcp != None):
        #    slaves  = {
        #         0x01: ModbusSlaveContext(...),
        #         0x02: ModbusSlaveContext(...),
        #         0x03: ModbusSlaveContext(...),
        #     }

        slaves = {}

        for currTcp in objectTcp["slaves"]:
            slaves[currTcp["slave_id"]] = ModbusSlaveContext(
                di=ModbusSequentialDataBlock(0, currTcp["list_di"]),
                co=ModbusSequentialDataBlock(0, currTcp["list_co"]),
                hr=ModbusSequentialDataBlock(0, currTcp["list_hr"]),
                ir=ModbusSequentialDataBlock(0, currTcp["list_ir"]))

        context = ModbusServerContext(slaves=slaves, single=False)

        identity = ModbusDeviceIdentification()
        identity.VendorName = 'Pymodbus'
        identity.ProductCode = 'PM'
        identity.VendorUrl = 'http://github.com/riptideio/pymodbus/'
        identity.ProductName = 'Pymodbus Server'
        identity.ModelName = 'Pymodbus Server'
        identity.MajorMinorRevision = version.short()

        while(True):
            try:
                print("\n[" + objectTcp["label"] + "] Starting TCP server on " +  objectTcp["ip_address"] + ":" +  str(objectTcp["port"]))
                StartTcpServer(context=context, identity=identity, address=(objectTcp["ip_address"], objectTcp["port"]))
                
            except Exception as err:
                print("[" + objectTcp["label"] + "] " + str(err))
                print("[" + objectTcp["label"] + "] Retry in 30 seconds")
                time.sleep(30)

# -------------------------------------------------------------
# --------- ModBus RTU ----------------------------------------
# -------------------------------------------------------------

def runServerRtu(name, objectRtu):

    time.sleep(1 + objectRtu["delay"] * 0.5)

    if(objectRtu != None):

        #    slaves  = {
        #         0x01: ModbusSlaveContext(...),
        #         0x02: ModbusSlaveContext(...),
        #         0x03: ModbusSlaveContext(...),
        #     }

        slaves = {}

        for currRtu in objectRtu["slaves"]:
            slaves[currRtu["slave_id"]] = ModbusSlaveContext(
                di=ModbusSequentialDataBlock(0, currRtu["list_di"]),
                co=ModbusSequentialDataBlock(0, currRtu["list_co"]),
                hr=ModbusSequentialDataBlock(0, currRtu["list_hr"]),
                ir=ModbusSequentialDataBlock(0, currRtu["list_ir"]))


        context = ModbusServerContext(slaves=slaves, single=False)

        identity = ModbusDeviceIdentification()
        identity.VendorName = 'Pymodbus'
        identity.ProductCode = 'PM'
        identity.VendorUrl = 'http://github.com/riptideio/pymodbus/'
        identity.ProductName = 'Pymodbus Server'
        identity.ModelName = 'Pymodbus Server'
        identity.MajorMinorRevision = version.short()

        while(True):
            try:
                print("\n[" + objectRtu["label"] + "] Starting RTU server on " + objectRtu["serial"])

                # Bytesize
                if(objectRtu["config"][0] == "7"):
                    bytesize_ = serial.SEVENBITS
                    print("[" + objectRtu["label"] + "] Bytesize: 7")
                elif(objectRtu["config"][0] == "8"):
                    bytesize_ = serial.EIGHTBITS
                    print("[" + objectRtu["label"] + "] Bytesize: 8")
                else:
                    bytesize_ = serial.EIGHTBITS
                    print("[" + objectRtu["label"] + "] Bytesize: 8")

                # Parity
                if(objectRtu["config"][1] == "E"):
                    parity_ = serial.PARITY_EVEN
                    print("[" + objectRtu["label"] + "] Parity: even")

                elif(objectRtu["config"][1] == "O"):
                    parity_ = serial.PARITY_ODD
                    print("[" + objectRtu["label"] + "] Parity: odd")

                elif(objectRtu["config"][1] == "N"):
                    parity_ = serial.PARITY_NONE
                    print("[" + objectRtu["label"] + "] Parity: none")

                # Stopbits
                if(objectRtu["config"][2:] == "1"):
                    stopbits_ = serial.STOPBITS_ONE
                    print("[" + objectRtu["label"] + "] Stopbits: 1")

                elif(objectRtu["config"][2:] == "1.5"):
                    stopbits_ = serial.STOPBITS_ONE_POINT_FIVE
                    print("[" + objectRtu["label"] + "] Stopbits: 1.5")

                elif(objectRtu["config"][2:] == "2"):
                    stopbits_ = serial.STOPBITS_TWO
                    print("[" + objectRtu["label"] + "] Stopbits: 2")

                # Provo ad aprire la seriale
                ser = serial.Serial(
                    port=objectRtu["serial"], 
                    baudrate=objectRtu["baudrate"],
                    bytesize=bytesize_,
                    parity=parity_,
                    stopbits=stopbits_)

                try:
                    ser.close()
                except:
                    pass

                ser.open()
                ser.close()

                StartSerialServer(
                    context=context, 
                    framer=ModbusRtuFramer, 
                    identity=identity, 
                    port=objectRtu["serial"], 
                    timeout=0.1, 
                    baudrate=objectRtu["baudrate"],
                    bytesize=bytesize_,
                    parity=parity_,
                    stopbits=stopbits_)

            except Exception as err:
                print("[" + objectRtu["label"] + "] " + str(err))
                print("[" + objectRtu["label"] + "] Retry in 30 seconds")
                time.sleep(30)

threadList = {}
threadList["TCP"] = []
threadList["RTU"] = []

running = True

# Funzione chiamata alla chiusura del programma
def onExit(eventCode, lineInfo):

    global threadList
    global running
    #print(eventCode)
    #print(lineInfo)

    print("SIGTERM")
    running = False

    try:
        # Uccido tutti i thread
        for thread in threadList["TCP"]:
            thread.terminate()
            print("Killing pid: " + str(thread.pid))

        for thread in threadList["RTU"]:
            thread.terminate()
            print("Killing pid: " + str(thread.pid))

        print("Server stopped")
    except:
        pass

def main():

    global threadList
    global running

    delay = 0

    for modbusHeadRtu in modbusHeadList["RTU"]:

        print("\n[" + modbusHeadRtu["label"] + "] Init head RTU: " + modbusHeadRtu["serial"])

        try:
            #threadRtu = threading.Thread(target=runServerRtu, args=(1, modbusHeadRtu))
            #threadRtu.setDaemon(True)   # Runs in background
            #threadRtu.start()

            modbusHeadRtu["delay"] = delay
            delay += 1

            threadRtu = Process(target=runServerRtu, args=(1, modbusHeadRtu))
            #threadRtu.daemon = True   # Runs in background
            threadRtu.start()

            print("[" + modbusHeadRtu["label"] + "] pid: " + str(threadRtu.pid))

            threadList["RTU"].append(threadRtu)

            print("[" + modbusHeadRtu["label"] + "] OK")

            time.sleep(0.03)

        except Exception as err:
            print(err)
            print("[" + modbusHeadRtu["label"] + "] FAIL")

    time.sleep(0.05)

    for modbusHeadTcp in modbusHeadList["TCP"]:

        print("\n[" + modbusHeadTcp["label"] + "] Init head TCP: " + modbusHeadTcp["ip_address"] + ":" + str(modbusHeadTcp["port"]))
        
        try:
            #threadTcp = threading.Thread(target=runServerTcp, args=(1, modbusHeadTcp))
            #threadTcp.setDaemon(True)   # Runs in background
            #threadTcp.start()
            
            modbusHeadTcp["delay"] = delay
            delay += 1

            threadTcp = Process(target=runServerTcp, args=(1, modbusHeadTcp))
            #threadTcp.setDaemon = True   # Runs in background
            threadTcp.start()

            print("[" + modbusHeadTcp["label"] + "] pid: " + str(threadTcp.pid))

            threadList["TCP"].append(threadTcp)

            print("[" + modbusHeadTcp["label"] + "] OK")

            time.sleep(0.03)

        except Exception as err:
            print(err)
            print("[" + modbusHeadTcp["label"] + "] FAIL")

    # Registro la chiamata della funzione allo stop del servizio
    if(platform == "linux" or platform == "linux2"):
        signal.signal(signal.SIGTERM, onExit)
        #signal.signal(signal.SIGINT, onExit)

    while(running):
        # Linux only
        if(platform == "linux" or platform == "linux2"):
            time.sleep(0.1)
        
        # Windows only
        else:
            command = input()

            if(command.find("exit") != -1):
                running = False
        
                # Uccido tutti i thread
                for thread in threadList["TCP"]:
                    thread.terminate()
                    print("killing pid: " + str(thread.pid))

                for thread in threadList["RTU"]:
                    thread.terminate()
                    print("killing pid: " + str(thread.pid))

                print("Server stopped")

if __name__ == '__main__':
    main()