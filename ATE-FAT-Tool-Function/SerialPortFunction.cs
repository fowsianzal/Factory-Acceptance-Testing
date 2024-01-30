using System.IO.Ports;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Azure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;

namespace ATE_FAT_Tool_Function
{
    
    public class SerialPortFunction
    {
        private readonly ILogger _logger;
        private static int timeout = 1000;


        public SerialPortFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SerialPortFunction>();
        }

        [Function("SerialPortFunction")]
        public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            var name = req.Query["name"];
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

           

            string deviceDetails = FindingSerialPortWithData();
            
            if (deviceDetails != null)
            {
                string jsonString = ParsingDeviceDataToJobject(deviceDetails);
                response.WriteString($"Json String is\n"+jsonString);

                Device dv=ConvertJsonObjToDeviceObj(jsonString);
                response.WriteString($"\n\nDevice objects are: \nProduct Name: {dv.Product} \nBootloader Version: {dv.BootloaderVersion} \nApplication Version: {dv.ApplicationVersion} \nSerial Number: {dv.SerialNumber} \nMac Address: {dv.MacAddress} \nBoot Filename: {dv.BootFilename} \nIP Address: {dv.IPaddress} \nBoot Delay: {dv.BootDelay} \nEthernet in MHz: {dv.Ethernet}");

                string document=  MongoDBInsertAndRetrieve(jsonString);
                response.WriteString($"\n\nDocument from Azure Cosmos DB for MongoDB:\n"+document);

                

            }
            else
            {
                response.WriteString("Please connect a device.");
            }

            

            return response;
        }

        [Function("GetAllIdAndDetails")]
        public HttpResponseData RunGetAllIdAndDetails([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            var name = req.Query["name"];

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            var keyValuePairs = GetAllIdAndNameAsKeyValuePair();

            response.WriteString("\n\nDevice details stored in Azure CosmosDB\n\n");
            foreach (var pair in keyValuePairs)
            {

                response.WriteString($"ID:{pair.Key}, Details:{pair.Value}\n");
            }

            return response;
        }

        [Function("GetFromMongoById")]
        public HttpResponseData RunGetFromMongoById([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "GetFromMongoById/{id}")] HttpRequestData req,string id)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            var name = req.Query["name"];

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            string singledoc = GetFromMongoById(id);
            response.WriteString($"\n\nSingle document with ID {id} from Azure CosmosDB:\n\n" + singledoc);

            return response;
        }

        static string FindingSerialPortWithData()
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
                        Console.WriteLine("Reading data from device...");
                        while (serialPort.BytesToRead < 2600)
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

        static Device ConvertJsonObjToDeviceObj(string jsonObj)
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
                    return device;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message); 
            }
            return null;
        }

        static string MongoDBInsertAndRetrieve(string jsonDevice)
        {
            try
            {
                var connectionString = "mongodb://ate-fat-comosdb-mongo:7L8AgMt0m4AQB2YlzawXgcWNs1cqkp7LFvkkAXLGnabGkwKssQG2ontJdh3LbHKQIztIl8wP3lI9ACDba7nZwg==@ate-fat-comosdb-mongo.mongo.cosmos.azure.com:10255/?ssl=true&replicaSet=globaldb&retrywrites=false&maxIdleTimeMS=120000&appName=@ate-fat-comosdb-mongo@";
                var client = new MongoClient(connectionString);
                var database = client.GetDatabase("DeviceDb");
                var collection = database.GetCollection<BsonDocument>("DeviceCollection");
                var document = BsonSerializer.Deserialize<BsonDocument>(jsonDevice);
                var serialNo = document["SerialNumber"].ToString();
                var filter = Builders<BsonDocument>.Filter.Eq("SerialNumber", serialNo);
                var existingDocument = collection.Find(filter).FirstOrDefault();
                if (existingDocument != null)
                {
                    Console.WriteLine($"\nThe document with Serial Number {serialNo} already exists in MongoDB. Skipping insertion.");
                    Console.WriteLine(existingDocument.ToString());
                    return existingDocument.ToString();
                }
                else
                {
                    // The document does not exist, so insert it
                    collection.InsertOne(document);
                    var docId = document["_id"].ToString();
                    Console.WriteLine($"\nThe document is inserted in MongoDB with document ID {docId}");
                    var documentId = ObjectId.Parse(docId);
                    filter = Builders<BsonDocument>.Filter.Eq("_id", documentId);
                    var insertedDocument = collection.Find(filter).FirstOrDefault();
                    Console.WriteLine(insertedDocument.ToString());
                    return insertedDocument.ToString();
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return null;
        }

        static List<KeyValuePair<string, string>> GetAllIdAndNameAsKeyValuePair()
        {
            try
            {
                var connectionString = "mongodb://ate-fat-comosdb-mongo:7L8AgMt0m4AQB2YlzawXgcWNs1cqkp7LFvkkAXLGnabGkwKssQG2ontJdh3LbHKQIztIl8wP3lI9ACDba7nZwg==@ate-fat-comosdb-mongo.mongo.cosmos.azure.com:10255/?ssl=true&replicaSet=globaldb&retrywrites=false&maxIdleTimeMS=120000&appName=@ate-fat-comosdb-mongo@";
                var client = new MongoClient(connectionString);
                var database = client.GetDatabase("DeviceDb");
                var collection = database.GetCollection<BsonDocument>("DeviceCollection");
                //var filter = FilterDefinition<BsonDocument>.Empty;
                var filter = Builders<BsonDocument>.Filter.Empty;
                //var results = collection.Distinct< ObjectId>("_id", filter).ToList();
                var results = collection.AsQueryable()
                                .Select(d => new KeyValuePair<string, string>(d["_id"].ToString(),"Product: " +d["Product"].ToString()+" Serial No: " + d["SerialNumber"].ToString()+" Mac Address: " + d["MacAddress"].ToString() ))
                                .ToList();
                Console.WriteLine("Method GetAllIdAndNameAsKeyValuePair");
                foreach (var result in results)
                {
                    Console.WriteLine("ID: {0}, Details: {1}", result.Key, result.Value);
                }

                return results;
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            return null;

        }

        static string GetFromMongoById(string id)
        {
            try
            {
                var connectionString = "mongodb://ate-fat-comosdb-mongo:7L8AgMt0m4AQB2YlzawXgcWNs1cqkp7LFvkkAXLGnabGkwKssQG2ontJdh3LbHKQIztIl8wP3lI9ACDba7nZwg==@ate-fat-comosdb-mongo.mongo.cosmos.azure.com:10255/?ssl=true&replicaSet=globaldb&retrywrites=false&maxIdleTimeMS=120000&appName=@ate-fat-comosdb-mongo@";
                var client = new MongoClient(connectionString);
                var database = client.GetDatabase("DeviceDb");
                var collection = database.GetCollection<BsonDocument>("DeviceCollection");
                var documentId = ObjectId.Parse(id);
                var filter = Builders<BsonDocument>.Filter.Eq("_id", documentId);
                var documents = collection.Find(filter).FirstOrDefault();


                Console.WriteLine("Single document GetFromMongoById");
                Console.WriteLine($"\nThe document with ID {documentId}");
                string json = documents.ToString();
                Console.WriteLine(json);
                return json;
            }
            catch(Exception ex) 
            {
                Console.WriteLine(ex.ToString());
            }
            return null;
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
        public string ApplicationVersion { get; set; }

        public Device(string product, string SerlNo, string bootdelay, string bootfilename, string IPAddress, string Version, string mcAddress, string ethernet, string applVer)
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



}
