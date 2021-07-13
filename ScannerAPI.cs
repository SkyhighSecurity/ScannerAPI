using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Amazon.S3;
using Amazon.S3.Model;

namespace ScannerAPI
    {

     public static class AVScan
    {
        [FunctionName("AVScan")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {

            //Parse the request header for parameters
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            string urlToScan = req.Query["url"];
            string s3uriString = req.Query["s3uri"];

            //To add/replace support for accepting a file directly, instead of pulling a blob pass the
            //file into a memory or file stream here

            // EDIT ICAPServer, ICAPClient, and sICAPPort below if you will run the code directly on Azure (or pass them as environment variables)
            string ICAPServer = Environment.GetEnvironmentVariable("ICAPSERVER");
            if (string.IsNullOrEmpty(ICAPServer))
            {
                ICAPServer = "3.234.210.152";
                log.LogInformation("No ICAP server specified, defaulting to " + ICAPServer);
            }
            string ICAPClient = Environment.GetEnvironmentVariable("ICAPCLIENT");
            if (string.IsNullOrEmpty(ICAPClient))
            {
                ICAPClient = "192.168.0.1";
                log.LogInformation("No default ICAP client specified, defaulting to " + ICAPClient);
            }
            string sICAPPort = Environment.GetEnvironmentVariable("ICAPPORT");
            if (string.IsNullOrEmpty(sICAPPort))
            {
                sICAPPort = "1344";
                log.LogInformation("No default ICAP port specified, defaulting to " + sICAPPort);
            }

            log.LogInformation("C# HTTP triggered for AVScan function");

            string ICAPServiceName = "avscan";
            int ICAPPort = int.Parse(sICAPPort);

            ICAPClient.ICAP icapper = new ICAPClient.ICAP(ICAPServer, ICAPPort, ICAPServiceName, ICAPClient);

            try
            {

                if (urlToScan != null)
                {
                    //if a URL is provided, use that first
                    MemoryStream responseStream = new MemoryStream(new WebClient().DownloadData(urlToScan));
                    string fileName = Path.GetFileName(urlToScan);
                    jsonScanResult ScanResult = icapper.scanFile(responseStream, fileName);

                    string jsonScanResultString = JsonConvert.SerializeObject(ScanResult);

                    log.LogInformation(jsonScanResultString);

                    responseStream.Dispose();
                    icapper.Dispose();

                    return new OkObjectResult(JsonConvert.SerializeObject(ScanResult));
                }
                else if (s3uriString != null)
                {
                    //if S3 URI is provided, use that
                    
                    AmazonS3Client s3Client = new AmazonS3Client();
                    GetObjectRequest s3GetRequest = new GetObjectRequest();

                    Uri s3uri = new Uri(s3uriString);

                    s3GetRequest.BucketName = s3uri.Host;


                    char[] trimChars = { '/' };
                    s3GetRequest.Key = s3uri.AbsolutePath.Trim(trimChars); //need to remove leading or trailing slashes

                    log.LogInformation("Got URI: " + s3uriString + ", bucket=" + s3uri.Host + "key=" + s3uri.AbsolutePath);
                    GetObjectResponse response = await s3Client.GetObjectAsync(s3GetRequest);

                    MemoryStream responseStream = new MemoryStream();
                    response.ResponseStream.CopyTo(responseStream);

                    jsonScanResult ScanResult = icapper.scanFile(responseStream, s3GetRequest.Key);  //scan the file

                    string jsonScanResultString = JsonConvert.SerializeObject(ScanResult);

                    log.LogInformation(jsonScanResultString);

                    responseStream.Dispose();
                    s3Client.Dispose();
                    icapper.Dispose();

                    return new OkObjectResult(JsonConvert.SerializeObject(ScanResult));
                    
                }
                else
                {
                    return new OkObjectResult("Error: Did not receive any targets to scan");
                }
                
            }
            catch (Exception ex)
            {
                log.LogInformation("Scan failure, unknown Error: " + ex);
                return new OkObjectResult("Could not complete scan.  Exception:" + ex);
                
            }
            //return new OkObjectResult(responseMessage);
        }

        class jsonScanResult
        {
            public string Filename;
            public bool Infected;
            public string InfectionName;
            public bool HasError;
            public string ErrorMessage;

        }

        class ICAPClient
        {

            public class ICAP : IDisposable
            {
                private String serverIP;
                private String clientIP;
                private int port;

                private Socket sender;

                private String icapService;
                private const String VERSION = "1.0";
                private const String USERAGENT = "Rest2ICAP";
                private const String ICAPTERMINATOR = "\r\n\r\n";
                private const String HTTPTERMINATOR = "0\r\n\r\n";

                private int stdPreviewSize;
                private const int stdRecieveLength = 4194304;
                private const int stdSendLength = 4096;

                private byte[] buffer = new byte[4096];
                private String tempString;

                public ICAP(String serverIP, int port, String icapService, String clientIP, int previewSize = -1)
                {
                    this.icapService = icapService;
                    this.serverIP = serverIP;
                    this.port = port;
                    this.clientIP = clientIP;

                    //Initialize connection
                    IPAddress ipAddress = IPAddress.Parse(serverIP);
                    IPEndPoint remoteEP = new IPEndPoint(ipAddress, port);

                    // Create a TCP/IP  socket.
                    sender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    sender.Connect(remoteEP);

                    if (previewSize != -1)
                    {
                        stdPreviewSize = previewSize;
                    }
                    else
                    {
                        String parseMe = getOptions();
                        Dictionary<string, string> responseMap = parseHeader(parseMe);

                        responseMap.TryGetValue("StatusCode", out tempString);
                        if (tempString != null)
                        {
                            int status = Convert.ToInt16(tempString);

                            switch (status)
                            {
                                case 200:
                                    responseMap.TryGetValue("Preview", out tempString);
                                    if (tempString != null)
                                    {
                                        stdPreviewSize = Convert.ToInt16(tempString);
                                    }; break;
                                default: throw new ICAPException("Could not get preview size from server");
                            }
                        }
                        else
                        {
                            throw new ICAPException("Could not get options from server");
                        }
                    }
                }

                public jsonScanResult scanFile(MemoryStream memStream, string Filename)
                {
                    int fileSize = (int)memStream.Length;

                    byte[] requestHeader = Encoding.ASCII.GetBytes("GET http://" + clientIP + "/" + Filename + " HTTP/1.1" + "\r\n" + "Host: " + clientIP + "\r\n\r\n");
                    byte[] responseHeader = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n" + "Transfer-Encoding: chunked\r\n\r\n");
                    int res_header = requestHeader.Length;
                    int res_body = res_header + responseHeader.Length;
                    int req_header = 0;
                    jsonScanResult result = new jsonScanResult();

                    
                    //If the file is small, set the preview length to the size of the file
                    int previewSize = stdPreviewSize;
                    if (fileSize < stdPreviewSize)
                    {
                        previewSize = fileSize;
                    }

                    //Build the main ICAPrequest
                    byte[] ICAPrequest = Encoding.ASCII.GetBytes(
                        "RESPMOD icap://" + serverIP + ":" + port + "/" + "respmod?profile=" + icapService + " ICAP/" + VERSION + "\r\n"
                        + "Connection: close\r\n"
                        + "Encapsulated: req-hdr=" + req_header + " res-hdr=" + res_header + " res-body=" + res_body + "\r\n"
                        + "Host: " + serverIP + "\r\n"
                        + "User-Agent: " + USERAGENT + "\r\n"
                        + "X-Client-IP: " + clientIP + "\r\n"
                        + "Allow: 204\r\n"
                        + "Preview: " + previewSize + "\r\n"
                        + "\r\n"
                    );

                    byte[] previewSizeHex = Encoding.ASCII.GetBytes(previewSize.ToString("X") + "\r\n");
                    sender.ReceiveTimeout = 600000;
                    sender.SendTimeout = 600000;
                    sender.Send(ICAPrequest);  //Send the initial ICAP request
                    sender.Send(requestHeader); //Send the forged client Request Header - MWG requires this
                    sender.Send(responseHeader); //Send the server response header (also forged)
                    sender.Send(previewSizeHex); //Send the hex value for the number of bytes to follow in the preview

                    byte[] chunk = new byte[previewSize]; //Send the preview chunk

                    memStream.Seek(0, SeekOrigin.Begin);  //Reset the stream position to the beginning

                    memStream.Read(chunk, 0, previewSize);
                    sender.Send(chunk);
                    sender.Send(Encoding.ASCII.GetBytes("\r\n"));

                    if (fileSize <= previewSize)  //If the file fits in the preview size send the terminator
                    {
                        sender.Send(Encoding.ASCII.GetBytes("0; ieof\r\n\r\n"));
                    }
                    else if (previewSize != 0)
                    {
                        sender.Send(Encoding.ASCII.GetBytes("0\r\n\r\n"));
                    }

                    // Parse the response to see if MWG bypasses based on preview or asks for the file
                    Dictionary<String, String> responseMap = new Dictionary<string, string>();
                    int status;

                    if (fileSize > previewSize)
                    {
                        String parseMe = getNextHeader(ICAPTERMINATOR);
                        responseMap = parseHeader(parseMe);

                        responseMap.TryGetValue("StatusCode", out tempString);
                        if (tempString != null)
                        {
                            status = Convert.ToInt16(tempString);

                            result.Filename = Filename;
                            result.HasError = false;

                            switch (status)  //Switching the status of the preview only, most of the time we'll get continue here
                            {
                                case 100:
                                    break;

                                case 200:
                                    result.Infected = false;
                                    result.HasError = true;
                                    return result;

                                case 204:
                                    result.Infected = false;
                                    return result;

                                case 304:
                                    result.Infected = true;
                                    string infectionName;
                                    responseMap.TryGetValue("X-Virus-Name", out infectionName);
                                    result.InfectionName = infectionName = "viaPreview";  
                                    return result;

                                default:
                                    result.Infected = false; // error
                                    result.HasError = true;
                                    string error = "Error: Unknown status code " + status;
                                    result.ErrorMessage = error;
                                    Console.WriteLine(DateTime.Now + ": " + error);
                                    return result;

                            }
                                
                        }
                    }

                    //Sending remaining part of file
                    if (fileSize > previewSize)
                    {
                        int offset = previewSize;
                        byte[] buffer = new byte[stdSendLength];
                        int n;

                        while ((n = memStream.Read(buffer, 0, stdSendLength)) > 0)
                        {
                            offset += n;  // offset for next reading
                            if (n > stdSendLength)
                            {
                                sender.Send(Encoding.ASCII.GetBytes(buffer.Length.ToString("X") + "\r\n"));
                                sender.Send(buffer);
                            }
                            else
                            {
                                byte[] lastBuffer = new byte[n];
                                Buffer.BlockCopy(buffer, 0, lastBuffer, 0, n);
                                sender.Send(Encoding.ASCII.GetBytes(lastBuffer.Length.ToString("X") + "\r\n"));
                                sender.Send(lastBuffer);
                            }
                            
                            sender.Send(Encoding.ASCII.GetBytes("\r\n"));
                        }
                        //Closing file transfer.
                        sender.Send(Encoding.ASCII.GetBytes("0\r\n\r\n"));
                    }
                    //fileStream.Close();

                    responseMap.Clear();
                    String response = getNextHeader(ICAPTERMINATOR);
                    responseMap = parseHeader(response);
                    responseMap.TryGetValue("StatusCode", out tempString);
                    if (tempString != null)
                    {
                        status = Convert.ToInt16(tempString);


                        if (status == 204)
                        {
                            result.Infected = false;  //Unmodified
                            result.HasError = false;
                            return result;
                        }

                        if (status == 200) //OK - The 200 ICAP status is ok, but the encapsulated HTTP status will likely be different
                        {

                            //Searching for: McAfee Virus Found Message
                            response = getNextHeader(HTTPTERMINATOR);

                            int titleStart = response.IndexOf("<title>", 0);
                            int titleEnd = response.IndexOf("</title>", titleStart);
                            String pageTitle = response.Substring(titleStart + 7, titleEnd - titleStart - 7);

                            if (pageTitle.Equals("McAfee Web Gateway - Notification"))
                            {
                                result.Infected = true;
                                String infectionName;
                                responseMap.TryGetValue("X-Virus-Name", out infectionName);

                                if (String.IsNullOrEmpty(infectionName)) {
                                    //Grab virus name from the message since its not included in the HEADER, MWG version dependent
                                    int x = response.IndexOf("Virus Name: </b>", 0);
                                    int y = response.IndexOf("<br />", x);
                                    infectionName = response.Substring(x + 16, y - x - 16);
                                    
                                }

                                result.InfectionName = infectionName;
                                return result;
                            }

                        }
                    }

                    responseMap.TryGetValue("X-Virus-Name", out tempString);
                    if (tempString !=null)
                    {
                        result.InfectionName = tempString;
                    }
                    
                    throw new ICAPException("Unrecognized or no status code in response header.");

                }

                /// <summary>
                /// Automatically asks for the servers available options and returns the raw response as a String.
                /// </summary>
                /// <returns>String of the raw response</returns>
                private string getOptions()
                {
                    byte[] msg = Encoding.ASCII.GetBytes(
                        "OPTIONS icap://" + serverIP + "/" + icapService + " ICAP/" + VERSION + "\r\n"
                        + "Host: " + serverIP + "\r\n"
                        + "User-Agent: " + USERAGENT + "\r\n"
                        + "Encapsulated: null-body=0\r\n"
                        + "\r\n");
                    sender.Send(msg);

                    return getNextHeader(ICAPTERMINATOR);
                }

                /// <summary>
                /// Receive an expected ICAP header as response of a request. The returned String should be parsed with parseHeader()
                /// </summary>
                /// <param name="terminator">Relative or absolute filepath to a file.</parm>
                /// <exception cref="ICAPException">Thrown when error occurs in communication with server</exception>
                /// <returns>String of the raw response</returns>
                private String getNextHeader(String terminator)
                {
                    byte[] endofheader = System.Text.Encoding.UTF8.GetBytes(terminator);
                    byte[] buffer = new byte[stdRecieveLength];

                    int n;
                    int offset = 0;
                    //stdRecieveLength-offset is replaced by '1' to not receive the next (HTTP) header.
                    while ((offset < stdRecieveLength) && ((n = sender.Receive(buffer, offset, 1, SocketFlags.None)) != 0)) // first part is to secure against DOS
                    {
                        offset += n;
                        if (offset > endofheader.Length + 13) // 13 is the smallest possible message (ICAP/1.0 xxx\r\n) or (HTTP/1.0 xxx\r\n)
                        {
                            byte[] lastBytes = new byte[endofheader.Length];
                            Array.Copy(buffer, offset - endofheader.Length, lastBytes, 0, endofheader.Length);
                            if (endofheader.SequenceEqual(lastBytes))
                            {
                                return Encoding.ASCII.GetString(buffer, 0, offset);
                            }
                        }
                    }

                    throw new ICAPException("Error in getNextHeader() method");
                }

                /// <summary>
                /// Given a raw response header as a String, it will parse through it and return a Dictionary of the result
                /// </summary>
                /// <param name="response">A raw response header as a String.</parm>
                /// <returns>Dictionary of the key,value pairs of the response</returns>
                private Dictionary<String, String> parseHeader(String response)
                {
                    Dictionary<String, String> headers = new Dictionary<String, String>();

                    /****SAMPLE:****
                     * ICAP/1.0 204 Unmodified
                     * Server: C-ICAP/0.1.6
                     * Connection: keep-alive
                     * ISTag: CI0001-000-0978-6918203
                     */
                    // The status code is located between the first 2 whitespaces.
                    // Read status code
                    int x = response.IndexOf(" ", 0);
                    int y = response.IndexOf(" ", x + 1);
                    String statusCode = response.Substring(x + 1, y - x - 1);
                    headers.Add("StatusCode", statusCode);

                    // Each line in the sample is ended with "\r\n". 
                    // When (i+2==response.length()) The end of the header have been reached.
                    // The +=2 is added to skip the "\r\n".
                    // Read headers
                    int i = response.IndexOf("\r\n", y);
                    i += 2;
                    while (i + 2 != response.Length && response.Substring(i).Contains(':'))
                    {
                        int n = response.IndexOf(":", i);
                        String key = response.Substring(i, n - i);

                        n += 2;
                        i = response.IndexOf("\r\n", n);
                        String value = response.Substring(n, i - n);

                        headers.TryAdd(key, value);
                        i += 2;
                    }
                    return headers;
                }

                /// <summary>
                /// A basic excpetion to show ICAP-related errors
                /// </summary>
                public class ICAPException : Exception
                {
                    public ICAPException(string message)
                        : base(message)
                    {
                    }

                }

                public void Dispose()
                {
                    sender.Shutdown(SocketShutdown.Both);
                    sender.Close();
                    sender.Dispose();
                    //fileStream.Close();
                    //throw new NotImplementedException();
                }
            }
        }
    }

    public class MVCConnection
    {
        public iam_tokenClass iam_token = new iam_tokenClass();
        public mvc_authinfoClass mvc_authinfo = new mvc_authinfoClass();
        public class iam_tokenClass
        {
            public string token_type;
            public DateTime expires_at;
            public string access_token;

        }

        public class mvc_authinfoClass
        {
            public string token_type;
            public string access_token;
            public string refresh_token;
            public string tenant_ID;
            public string tenant_Name;
            public string userID;
            public string email;
            public string users;
            public DateTime expires_at;
        }
        public bool isAuthenticated()
        {
            if (string.IsNullOrEmpty(iam_token.access_token) || DateTime.Now > iam_token.expires_at)
            { 
                return false;
            }
            else
            {
                return true;
            }
                
        }
        public async Task<bool> AuthenticateAsync(string username, string password, string bpsTenantid, string env, ILogger log)
        {
            string iam_url = "https://iam.mcafee-cloud.com/iam/v1.1/token";  //hard coded
            
            if (string.IsNullOrEmpty(env)) { env = "www.myshn.net"; }
            

            var iam_payload = new Dictionary<string, string>
                    {
                        { "client_id", "0oae8q9q2y0IZOYUm0h7" },
                        { "grant_type", "password" },
                        { "username", username },
                        { "password", password },
                        { "scope", "shn.con.r web.adm.x web.rpt.x web.rpt.r web.lst.x web.plc.x web.xprt.x web.cnf.x uam:admin" },
                        { "tenant_id", bpsTenantid },
                    };

            try  //Authenticate to McAfee IAM
            {
                HttpClient client = new HttpClient();
                var iam_data = new FormUrlEncodedContent(iam_payload);
                var iam_response = await client.PostAsync(iam_url, iam_data);

                if (iam_response.StatusCode != HttpStatusCode.OK)
                {
                    //Got something other than OK, error out
                    log.LogInformation("Unsuccessful authentication of " + username + "to McAfee IAM.  HTTP Status: " + iam_response.StatusCode.ToString());
                    return false;

                }
                else
                {
                    var iam_responseString = await iam_response.Content.ReadAsStringAsync();
                    var iam_responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(iam_responseString);

                    iam_token.access_token = iam_responseData["access_token"];
                    iam_token.expires_at = DateTime.Now.AddSeconds(int.Parse(iam_responseData["expires_in"]));
                    iam_token.token_type = iam_responseData["token_type"];
                   
                    //TODO write token information to class
                    log.LogInformation("Successful authentication of " + username + "to McAfee IAM and fetch of iam_token");

                }
            }
            catch (Exception e)
            {
                log.LogInformation("Exception in IAM authentication: " + e.Message);
                return false;
            }

            string mvc_url = "https://" + env + "/neo/neo-auth-service/oauth/token?grant_type=iam_token";
            try //Authenticate to MVISION Cloud
            {
                HttpClient mvc_client = new HttpClient();

                var mvc_request = new HttpRequestMessage()
                {
                    RequestUri = new Uri(mvc_url),
                    Method = HttpMethod.Post
                };
                mvc_request.Headers.Add("x-iam-token", iam_token.access_token);

                var mvc_response = await mvc_client.SendAsync(mvc_request);

                if (mvc_response.StatusCode != HttpStatusCode.OK)
                {
                    //Got something other than OK, error out
                    log.LogInformation("Unsuccessful authentication of " + username + "to MVISION Cloud.  HTTP Status: " + mvc_response.StatusCode.ToString());
                    return false;
                }
                else
                {
                    var mvc_responseString = await mvc_response.Content.ReadAsStringAsync();
                    var mvc_responseData = JsonConvert.DeserializeObject<Dictionary<string,string>>(mvc_responseString);

                    mvc_authinfo.token_type = mvc_responseData["token_type"];
                    mvc_authinfo.access_token = mvc_responseData["access_token"];
                    mvc_authinfo.refresh_token = mvc_responseData["refresh_token"];
                    mvc_authinfo.tenant_ID = mvc_responseData["tenantID"];
                    mvc_authinfo.tenant_Name = mvc_responseData["tenantName"];
                    mvc_authinfo.userID = mvc_responseData["userId"];
                    mvc_authinfo.email = mvc_responseData["email"];
                    mvc_authinfo.expires_at = DateTime.Now.AddSeconds(int.Parse(mvc_responseData["expires_in"]));

                    log.LogInformation("Successful authentication of " + username + "to MVISION Cloud, got access token.");
                    return true;
                }

            }
            catch (Exception e)
            {
                log.LogInformation("Exception in MVISION Cloud authentication: " + e.Message);
                return false;
            }

        }
    }
    public static class DLPScan
    {
        static MVCConnection conn = new MVCConnection();
        [FunctionName("DLPScan")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            string urlToScan = req.Query["url"];
            string fileName = Path.GetFileName(urlToScan);

            string env = "www.myshn.net";  //TODO: get from env variable
            string policyid = "520065"; //TODO: get form env variable

            MemoryStream responseStream;

            if (conn.isAuthenticated())
            {
                log.LogInformation("Already authenticated...");
                return new OkObjectResult("Already authenticated...");
            }
            else
            {
                bool isAuthenticated = await conn.AuthenticateAsync("nate@mvision-ebc.com", "9hy%QP1hxoX&", "A9DD97B4-FBB7-49F8-80A0-8A2164A1E17C", "", log);
                log.LogInformation("Authenticating, result: " + isAuthenticated.ToString());
            }

            string mvc_url = "https://" + env + "/neo/zeus/v1/admin/content-parser/policy/evaluation/silo/" + conn.mvc_authinfo.tenant_ID + "/" + "1";
            log.LogInformation("Calling MVC API: " + mvc_url);

            try //Fetch file and DLP API Request
            {
                responseStream = new MemoryStream(new WebClient().DownloadData(urlToScan));
                log.LogInformation("Sucessfully fetched " + fileName);

                HttpClient mvc_client = new HttpClient();
                mvc_client.DefaultRequestHeaders.Add("x-access-token", conn.mvc_authinfo.access_token);
                mvc_client.DefaultRequestHeaders.Add("x-refresh-token", conn.mvc_authinfo.refresh_token);

                var formData = new MultipartFormDataContent();
                formData.Add(new ByteArrayContent(responseStream.ToArray()), "file", fileName);
                formData.Add(new StringContent("1"), "numOfTimes");
                formData.Add(new StringContent(policyid), "policy_ids");

                var mvc_response = await mvc_client.PostAsync(mvc_url, formData);

                var mvc_responseString = await mvc_response.Content.ReadAsStringAsync();
                log.LogInformation(mvc_responseString);
                var mvc_responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(mvc_responseString);

                log.LogInformation("Processed DLP Policy Evaluation: Filename=" + mvc_responseData["fileName"] + " Policy Name=" + mvc_responseData["policy_name"] + " Result=" + mvc_responseData["evaluation_result"]);

            }
            catch (Exception e)
            {
                log.LogInformation("Exception in DLP API call: " + e.Message);

            }


                return new OkObjectResult("DLP Result:");
           
        }

     }
}

