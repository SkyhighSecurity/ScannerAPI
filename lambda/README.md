# Lambda Function for ScannerAPI

This Lambda function is designed to be triggered by file uploads to an S3 bucket that trigger the Lambda function by SQS message.
It takes the s3 Bucket and Key (file) from the message and performs the following:

1. Call the ScannerAPI container to submit the file to MWG for scanning.
2. Retrieves existing tags on the file
3. if the file is clean, it tags the file with "Scanned" = "clean"
4. if the scan process failed, it tags the file with "Scanned" = "failed"
5. otherwise, the file was deemed malicious, in that case it is deleted from S3 and
6. also a notification is send to the SNS topic to say the file was deleted and what infection was detected.

# Deployment

In order to deploy, you need to log in to AWS using the aws cli and then follow the below steps:

Deployment to AWS requires an S3 bucket used to host the lambda function.

run
```bash
./1-create-bucket.sh
```

This will write the local file bucket-name.txt to store the bucket name for the rest of the scripts.

Then, to deploy the lambda function, use
```bash
./2-deploy.sh
```

Refer to AWS documentation on how to set up the SQS queue to trigger this function on new files being uploaded to a bucket.

## Configuration

Once installed, as the lambda function requires access to S3 to read/write (delete and tag) you need to set environment variables on the lambda function
Optionally, if you want to receive notifications for Deletions and Errors, set the NotifyTopicARN environment variable.


| Environment Variable | Mandatory? | Description |
| - | - | - |
| S3Key | Y | Key for IAM user with write access to S3 |
| S3Secret | Y | Secret for IAM user with write access to S3 |
| ScannerAPI | Y | IP Address for ScannerAPI container |
| NotifyTopicARN | N | ARN for SNS notification topic |


## VPC and Endpoints

To deploy in AWS, this was tested by using ECS to deploy the the ScannerAPI container into a VPC. The lambda function was
then also added to the VPC in order to be able to make REST calls to the ScannerAPI container endpoint.

Additionally, as the lambda function was added to the VPC, it lost it's ability to communicate with S3 and SNS (as per AWS policy).
To resolve this, endpoints were added to the VPC for both S3 and SNS. See AWS documentation for instructions on how to do this.

# Updating the Lambda function

If you modify the Lambda node function, simply re-run
```bash
./2-deploy.sh
```
in order to deploy your changes.

# Testing

You should be able to simply test the function by uploading to the S3 bucket. However, if you want to quickly make changes and
trigger the lambda from the command line, the event.json file contains a templated payload you can send directly to the lambda
function. Modify this to provide the s3 bucket and key (other fields are ignored) and use this script to post it to the lambda function:
```bash
./3-test.sh
```

# Decommissioning

If you want to tear down the lambda function and remove the s3 bucket for lambda artifacts use this script:
```bash
./4-cleanup.sh
```