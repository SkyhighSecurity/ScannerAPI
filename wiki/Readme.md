# Introduction
This project provides a REST API front-end to a back-end ICAP server running on McAfee Web Gateway. The C# code can be run directly as an Azure Function or anywhere else that can run a Docker container to allow you to create a scalable microservice to process synchronous anti-malware and DLP requests.

# Installation

## Docker
When running as a container, you'll need to pass a few variables so the API knows where to make its ICAP requests.

For example:
`docker run -it -p 80:80 --name MalwareScanner -e ICAPSERVER=YOUR_ICAP_SERVER -e ICAPPORT=YOUR_ICAP_PORT -e ICAPCLIENT=YOUR_ICAP_CLIENT malwarescanner:latest
`
Note: The ICAPCLIENT variable is what IP/Host will be submitted to your ICAP server.

## As an Azure function

Simply copy REST2ICAP.cs into your Azure function and edit the ICAPServer, ICAPClient, and sICAPPort parameters for your configuration.

# Usage


# FAQ
### Why not just call the ICAP server directly?
While ICAP is a standard defined by RFC 3507, it is not easy to use for modern applications due to its unique style of responses which are intended for display to end users. There are no standardized libraries for dealing with ICAP responses, so this would have to be written in to every application that needs anti-Malware/DLP functionality. Using this project allows developers to use a familiar REST API.