# Introduction
This project provides a REST API front-end to a back-end ICAP server running on McAfee Web Gateway. The API accepts a URL as an input
The C# code can be run directly as an Azure Function or anywhere else that can run a Docker container to allow you to create a scalable microservice to process synchronous anti-malware and DLP requests.

# Installation

## Docker
When running as a container, you'll need to pass a few variables so the API knows where to make its ICAP requests.

For example:

`docker run -it -p [port]:80 --name MalwareScanner -e ICAPSERVER=[YOUR_ICAP_SERVER] -e ICAPPORT=[YOUR_ICAP_PORT] -e ICAPCLIENT=[YOUR_ICAP_CLIENT] terratrax/rest2icap:latest
`

Note: The ICAPCLIENT variable is what IP/Host will be submitted to your ICAP server.

## Azure serverless function

Simply copy REST2ICAP.cs into your Azure function and edit the ICAPServer, ICAPClient, and sICAPPort parameters for your configuration.

# Usage

REST2ICAP relies on the Microsoft Azure Functions framework, so the endpoint will always take the form of:

`http://[CONTAINERHOST]:[PORT]/api/AVScan?url=[URL_TO_SCAN]`

The API will return a JSON formatted result like this:

`{"Filename":"putty-64bit-0.75-installer.msi","Infected":false,"InfectionName":null,"HasError":false,"ErrorMessage":"false flag set by responsemap"}`


# FAQ
### Why not just call the ICAP server directly?
While ICAP is a standard defined by RFC 3507, it is not easy to use for modern applications due to its unique style of responses which are intended for display to end users though a Web Gateway. There are no standardized libraries for dealing with ICAP responses, so this would have to be written in to every application that needs anti-Malware/DLP functionality. Using this project allows developers to use a familiar REST API.

### Can I use HTTPS?
Since the caller and the API endpoint do not exchange the file directly (only the URL and result are exchange) there is no need for HTTPS for most use cases.  However, if you would still like to use HTTP, implemented reverse proxy or API gateway would be best practices.

### Can I submit a file directly via HTTP stream?
This is currently not implemented.  A more secure way to perform this function that avoids the need for a reverse proxy would be to initially save the file to blob storage, generate a pre-signed URL (or SAS token) for the file, and submit that URL to the API for scanning.  If you have a use case to submit the file directly please contact the project team and we can consider adding it.

