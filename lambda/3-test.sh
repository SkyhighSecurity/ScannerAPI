#!/bin/bash
set -eo pipefail
FUNCTION=$(aws cloudformation describe-stack-resource --stack-name scannerbridge --logical-resource-id function --query 'StackResourceDetail.PhysicalResourceId' --output text)
if [[ $(aws --version) =~ "aws-cli/2." ]]; then PAYLOAD_PROTOCOL="fileb"; else  PAYLOAD_PROTOCOL="file"; fi;

#while true; do
#  aws lambda invoke --function-name $FUNCTION --payload $PAYLOAD_PROTOCOL://event.json out.json
#  cat out.json
#  echo ""
#  sleep 2
#done


echo $FUNCTION
echo $PAYLOAD_PROTOCOL
aws lambda invoke --function-name $FUNCTION --payload $PAYLOAD_PROTOCOL://event.json out.json
cat out.json

