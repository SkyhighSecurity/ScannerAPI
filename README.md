# Introduction
This project provides a REST API front-end to McAfee anti-malware and data classification services provided my McAfee Web Gateway (antimalware) and MVISION Cloud (data classification). The API accepts a URL as an input and provides a scan response in JSON format.
The C# code can be run directly as an Azure Function or anywhere else that can run a Docker container to allow you to create a scalable microservice to process synchronous anti-malware and classification/DLP requests.

# Installation

## Docker
When running as a container, you'll need to pass a few variables so the API knows where to make its ICAP requests.

For example:

`docker run -it -p [port]:80 --name MalwareScanner -e ICAPSERVER=[YOUR_ICAP_SERVER] -e ICAPPORT=[YOUR_ICAP_PORT] -e ICAPCLIENT=[YOUR_ICAP_CLIENT] terratrax/scannerapi:latest'

Note: The ICAPCLIENT variable is what IP/Host will be submitted to your ICAP server (for logging purposes only) which much be reachable from the container

## Azure serverless function

Simply copy ScannerAPI.cs into your Azure function and edit the ICAPServer, ICAPClient, and sICAPPort parameters for your configuration.

# Usage

ScannerAPI relies on the Microsoft Azure Functions framework, so the endpoint will always take the form of:

`http://[HOST]:[PORT]/api/[endpoint]?usefilecache=[True|False]&[url|s3uri]=[URL_OR_S3URI_TO_SCAN]`

Presently the AVScan endpoint is supported and the DLPScan endpoint is experimental.

# Usage Examples

	Example for malware scanning an http object:

	'http://myhost:80/api/AVScan?url=http://mystore.myco.com/files/abc123.zip'

	Example for malware scanning an object in AWS S3:

	'http://myhost:80/api/AVScan?s3uri=s3://mybucket/abc123.zip'

	Scanning a large file?  Instruct the API to cache the file to disk instead of memory by passing usefilecache=True (case sensitive):

	'http://myhost:80/api/AVScan?usefilecache=True&url=http://mystore.myco.com/files/abc123.zip'

The API will return a JSON formatted result like this:

`{"Filename":"putty-64bit-0.75-installer.msi","Infected":false,"InfectionName":null,"HasError":false,"ErrorMessage":null}`

Data classification is currently experimental / undocumented and you will need pass MVISION Cloud credentials and DLP policy ID as environent variables.

# FAQ
### Why not just call the ICAP server directly?
While ICAP is a standard defined by RFC 3507, it is not easy to use for modern applications due to its unique style of responses which are intended for display to end users though a Web Gateway. There are no standardized libraries for dealing with ICAP responses, so this would have to be written in to every application that needs anti-Malware/DLP functionality. Using this project allows developers to use a familiar REST API.

### Can I use HTTPS?
Since the caller and the API endpoint do not exchange the file directly (only the URL and result are exchanged) there is no need for HTTPS for most use cases.  However, if you would still like to use HTTPS, implementing a reverse proxy or API gateway would be recommended best practices.

### Can I submit a file directly via HTTP stream?
This is currently not implemented.  A more secure way to perform this function that avoids the need for a reverse proxy would be to initially save the file to blob storage, generate a pre-signed URL (or SAS token) for the file, and submit that URL to the API for scanning.  If you have a use case to submit the file directly please contact the project team and we can consider adding it.

### How do I utilize the DLP / data classification function
This is currently experimental and undocumented.

### When running on Amazon ECS or other hosted container services the container stops immediately
Specify the entry point "/azure-functions-host/Microsoft.Azure.WebJobs.Script.WebHost" in the your services container configuration.

### How I write temporary files (when scans specify usediskcache=True) to storage outside the container
Use a volume so that the containers /tmp directory references an external volume.