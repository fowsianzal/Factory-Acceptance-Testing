using System.IO.Ports;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Threading;

internal class Program
{
    private static int timeout = 1000;
    private static ManualResetEvent dataReceivedEvent = new ManualResetEvent(false);
    private static void Main(string[] args)
    {
        string deviceDetails= FindingSerialPortWithData();
        //FindingSerialPortWithDataUsingEventHandlers();

        if (deviceDetails != null)
        {
            string jsonString = ParsingDeviceDataToJobject(deviceDetails);

            ConvertJsonObjToDeviceObj(jsonString);

            InsertAndRetrieveMongo(jsonString);
        }



    }

    static string FindingSerialPortWithData()
    {
        Console.WriteLine("Scanning for serial ports with data...");
        //List<SerialPort> lstPorts = new List<SerialPort>();
        string[] strPortArray = SerialPort.GetPortNames();
        foreach (string strPort in strPortArray)
        {
            try
            {

                SerialPort serialPort = new SerialPort(strPort, 19200, Parity.None, 8, StopBits.One);
                serialPort.ReadTimeout = timeout;
                serialPort.WriteTimeout = timeout;

                serialPort.Open();

                serialPort.WriteLine("reset");
                Thread.Sleep(timeout);

                if (serialPort.BytesToRead > 0)
                {
                    
                    Console.WriteLine("Found data on port " + strPort);
                    while (serialPort.BytesToRead < 2800)
                    { }
                    string deviceDetails = serialPort.ReadExisting();
                    return deviceDetails;
                    
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error on port " + strPort + " " + ex.Message);
            }
            finally
            {
                SerialPort port = new SerialPort();
                port.PortName = strPort;
                port.Close();
            }
        }
        return null;
    }

    static string FindingSerialPortWithDataUsingEventHandlers()
    {
        Console.WriteLine("Scanning for serial ports with data...");
        string[] strPortArray = SerialPort.GetPortNames();
        foreach (string strPort in strPortArray)
        {
            try
            {

                SerialPort serialPort = new SerialPort(strPort, 19200, Parity.None, 8, StopBits.One);
                serialPort.ReadTimeout = timeout;
                serialPort.WriteTimeout = timeout;

                serialPort.Open();

                serialPort.WriteLine("reset");
                Thread.Sleep(timeout);

                if (serialPort.BytesToRead > 0)
                {

                    Console.WriteLine("Found data on port " + strPort);
                    serialPort.DataReceived += new SerialDataReceivedEventHandler(SerialPort_DataReceived);
                    dataReceivedEvent.WaitOne();
                    //while (serialPort.BytesToRead < 2800)
                    //{ }
                    //string deviceDetails = serialPort.ReadExisting();
                    //return deviceDetails;

                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error on port " + strPort + " " + ex.Message);
            }
            finally
            {
                SerialPort port = new SerialPort();
                port.PortName = strPort;
                port.Close();
            }
        }
        return null;
    }



    static string ParsingDeviceDataToJobject(string deviceDetail)
    {
        try
        {
            JObject mergedObj = new JObject();

            string[] lines = deviceDetail.Split('\n');
            bool startParsing = false;


            foreach (string line in lines)
            {
                if (line.StartsWith("\rOmnitronics Bootloader"))
                {
                    string pattern = @"\d+\.\d+\.\d+";
                    Match match = Regex.Match(line, pattern);
                    if (match.Success)
                    {
                        string version = match.Value;
                        mergedObj["version"] = version;

                    }
                    startParsing = true;
                }

                if (startParsing)
                {

                    if (line.StartsWith("\rIP:"))
                    {
                        string pattern = @"(?<=IP:\s)\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}";
                        Match match = Regex.Match(line, pattern);
                        if (match.Success)
                        {
                            string ipaddress = match.Value;
                            mergedObj["IPaddress"] = ipaddress;

                        }
                    }
                    if (line.StartsWith("\rBoot Mac 0"))
                    {
                        string pattern = @"(?<=: )\S+";
                        Match match = Regex.Match(line, pattern);
                        if (match.Success)
                        {
                            string macaddress = match.Value;
                            mergedObj["MacAddress"] = macaddress;

                        }
                    }
                    if (line.StartsWith("\rEthernet connected at"))
                    {
                        string pattern = @"\d+";
                        Match match = Regex.Match(line, pattern);
                        if (match.Success)
                        {
                            string ethernet = match.Value;
                            mergedObj["Ethernet"] = ethernet;

                        }
                    }
                    if (line.Contains("Coldfire Application Version"))
                    {
                        string pattern = @"(?<=Version\s\.\.\.\s)\d+\.\d+\.\d+";
                        Match match = Regex.Match(line, pattern);
                        if (match.Success)
                        {
                            string ApplicationVer = match.Value;
                            mergedObj["ApplicationVersion"] = ApplicationVer;

                        }
                    }

                    if (line.StartsWith("RTEMS SHELL (Ver.1.0-FRC):/dev/console. Apr  5 2012. 'help' to list commands"))
                    {
                        break;
                    }


                    string[] Arr1 = line.Split(":");
                    if (Arr1.Length == 2)
                    {
                        string key = Arr1[0].Trim().Replace(" ", "");
                        string value = Arr1[1].Trim();
                        mergedObj[key] = value;

                    }

                }
            }

            string mergedJson = mergedObj.ToString();
            Console.WriteLine("\nMerged Json Object is:");
            Console.WriteLine(mergedJson);
            return mergedJson;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
        return null;
    }

    static void ConvertJsonObjToDeviceObj(string jsonObj)
    {
        try
        {
            if (JsonConvert.DeserializeObject<Device>(jsonObj) is Device device)
            {
                Console.WriteLine("\nDevice Objects:");
                Console.WriteLine($"Product Name: {device.Product}");
                Console.WriteLine($"Serial Number: {device.SerialNumber}");
                Console.WriteLine($"Boot Delay: {device.BootDelay}");
                Console.WriteLine($"Boot Filename: {device.BootFilename}");
                Console.WriteLine($"IP Address: {device.IPaddress}");
                Console.WriteLine($"Bootloader Version: {device.BootloaderVersion}");
                Console.WriteLine($"Mac Address: {device.MacAddress}");
                Console.WriteLine($"Ethernet in MHz: {device.Ethernet}");
                Console.WriteLine($"Application Version: {device.ApplicationVersion}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message); return;
        }
    }

    static void InsertAndRetrieveMongo(string jsonDevice)
    {
        try
        {
            var connectionString = "mongodb://localhost:27017";
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase("DeviceDb");
            var collection = database.GetCollection<BsonDocument>("DeviceCollection");
            var document = BsonSerializer.Deserialize<BsonDocument>(jsonDevice);
            collection.InsertOne(document);
            var docId = document["_id"].ToString();

            Console.WriteLine($"\nThe document is inserted in MongoDB with document ID {docId}");
            var documentid = ObjectId.Parse(docId.ToString());
            var filter = Builders<BsonDocument>.Filter.Eq("_id", documentid);
            var retrieveddoc = collection.Find(filter).FirstOrDefault();
            Console.WriteLine($"\n Retrieved document from MongoDB with documentID {docId}");
            Console.WriteLine(retrieveddoc.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private static void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        SerialPort port = (SerialPort)sender;
        try
        {


            while (port.BytesToRead < 2800) { }
            string DevDetail = port.ReadExisting();
            //Console.WriteLine(DevDetail);

            string jsonString = ParsingDeviceDataToJobject(DevDetail);

            ConvertJsonObjToDeviceObj(jsonString);

            InsertAndRetrieveMongo(jsonString);
            dataReceivedEvent.Set();

        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}

 

   

    public class Device
    {
        public string Product { get; set; }
        public string SerialNumber { get; set; }
        public string BootDelay { get; set; }
        public string BootFilename { get; set; }
        public string IPaddress { get; set; }
        public string BootloaderVersion { get; set; }
        public string MacAddress { get; set; }
        public string Ethernet { get; set; }
        public string ApplicationVersion {  get; set; }
        
        public Device( string product, string SerlNo,  string bootdelay, string bootfilename,  string IPAddress, string Version,string mcAddress,string ethernet,string applVer)
        {            
            Product = product;
            SerialNumber = SerlNo;
            BootDelay = bootdelay;
            BootFilename = bootfilename;
            IPaddress = IPAddress;
            BootloaderVersion = Version;
            MacAddress = mcAddress;
            Ethernet = ethernet;
            ApplicationVersion = applVer;
            
        }
    }

