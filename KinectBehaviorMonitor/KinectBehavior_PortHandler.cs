using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
namespace KinectBehaviorMonitor
{
    class KinectBehavior_PortHandler
    {
        string ComPortString = "COM5";
        String lastTreat = "";
        string serialBuf = "";
        bool readPortOpen = false;
        SerialPort serialPort1 = new SerialPort();
        double lastSerialRead = 0;
        KinectBehavior_FileHandler fileHandler;
        bool usingSerial = false;

        public KinectBehavior_PortHandler(KinectBehavior_FileHandler fh) { //constructor
            fileHandler = fh;
            if (Environment.UserName == "fetzlab" && SerialPort.GetPortNames().Any(x => string.Compare(x, ComPortString, true) == 0))
            {
                usingSerial = true;
                serialPort1.PortName = ComPortString;
                serialPort1.BaudRate = 9600;
                serialPort1.Open();
                serialPort1.ReadTimeout = 20;
            }
            else
            {
                Console.WriteLine("no comPort");
            }
        }

        public void sendSerialTreat()
        {
            char[] test = new char[1];
            test[0] = 'A';

            if (usingSerial)
            {
                //SerialPort port1 = new SerialPort(ComPortString, 9600);
               // port1.Open();
                if (!serialPort1.IsOpen)
                {
                    serialPort1.Open();
                }
                serialPort1.Write(test, 0, 1);
                // user.Text = port1.ReadByte().ToString();

                serialPort1.Close();
            }
         

        }

        public bool checkSerialInput(double totalSecondsElapsed)
        {

            bool receivedTreat = false;
            if (usingSerial) { 
            if ((lastSerialRead) < totalSecondsElapsed - .5)
            {
                try
                {
                    lastTreat = serialPort1.ReadLine();
                }
                catch (Exception ex)
                {
                    lastTreat = null;
                }
                lastSerialRead = totalSecondsElapsed;
                if (lastTreat != null)
                {
                    receivedTreat = true;
                    fileHandler.SaveEventData(totalSecondsElapsed, lastTreat); 
                }

            }
               }
            return receivedTreat;
        }
           

    }
}
