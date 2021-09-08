
const async = require('async');
const superagent = require('superagent');
const AWS = require('aws-sdk');

const scanServer = process.env.ScannerAPI;
const notifyTopicARN = process.env.NotifyTopicARN;

/**
 * A Lambda function that logs the payload received from SQS.
 */

//
// Handle an individual record from SQS
//
//     "s3":{
//         "s3SchemaVersion":"1.0",
//         "configurationId":"FileUploaded",
//         "bucket":{
//         "name":"agency-a-bucket",
//         "ownerIdentity":{
//             "principalId":"A1GZDEY2770U2R"
//         },
//         "arn":"arn:aws:s3:::agency-a-bucket"
//     },
//     "object":{
//         "key":"2213415-1920x1080-1483805249791.jpg",
//         "size":854023,
//         "eTag":"fb12ad84583422c9ba6340dccb7d656c",
//         "sequencer":"006131AE8DF7F6A48D"
//     }
// }
//

function encode(filename) {
    const encodings = {
        '\+': "%2B",
        '\!': "%21",
        '\"': "%22",
        '\#': "%23",
        '\$': "%24",
        '\&': "%26",
        '\'': "%27",
        '\(': "%28",
        '\)': "%29",
        '\*': "%2A",
        '\,': "%2C",
        '\:': "%3A",
        '\;': "%3B",
        '\=': "%3D",
        '\?': "%3F",
        '\@': "%40",
    };

    return filename.replace(
        /([+!"#$&'()*+,:;=?@])/img,
        match => encodings[match]
    );
}

function handleRecord(s3, record, callback) {
    const s3record = record.s3;
    console.log(s3record);
    if (!s3record) { return callback(null); } // silent failure

    //console.log(s3.bucket);

    const bucket = s3record.bucket && s3record.bucket.name; //&& decodeURIComponent(s3record.bucket.name.replace(/\+/g, ' '));
    var file = null;
    // stupid aws puts "object" as a key! DOH!
    for (const [key, value] of Object.entries(s3record)) {
        if (key === 'object') {
            file = value.key; // && decodeURIComponent(value.key.replace(/\+/g, ' '));
        }
    }
    if (!bucket || !file) { return callback(null); } // silent failure

    async.auto({
        scan: function(callback) {
            console.log('get http://' + scanServer + '/api/AVScan?usefilecache=True&s3uri=s3://' + bucket + '/' + file;
            superagent
                .get('http://' + scanServer + '/api/AVScan?usefilecache=True&s3uri=s3://' + bucket + '/' + file
                .set('Accept', 'application/json')
                .then(res => {
                    console.log(`statusCode: ${res.status}`, res.body);
                    try {
                        const decoded = JSON.parse(res.body);
                        if (decoded) {
                            res.body = decoded;
                        }
                        return callback(null, res);
                    } catch (error) {
                        console.error(error);

                        if (!notifyTopicARN) {
                            return callback(error);
                        }

                        const params = {
                            Message: `Could not scan ${bucket}/${file} : ${res.body}`,
                            TopicArn: notifyTopicARN
                        };

                        // Create promise and SNS service object
                        var publishTextPromise = new AWS.SNS({ apiVersion: '2010-03-31' }).publish(params).promise();

                        // Handle promise's fulfilled/rejected states
                        publishTextPromise.then(function(data) {
                            console.log(`Message ${params.Message} sent to the topic ${params.TopicArn}`);
                            console.log("MessageID is " + data.MessageId);
                            callback(null);    // silent fail this one
                        }).catch(function(err) {
                            console.log(`Unable to send message`);
                            console.error(err);
                            callback(null);   // silent fail this one
                        });
                    }
                    //callback(null); // silent fail this one
                    // {"Filename":"putty-64bit-0.75-installer.msi","Infected":false,"InfectionName":null,"HasError":false,"ErrorMessage":null}
                })
                .catch(error => {
                    console.error(error);

                    if (!notifyTopicARN) {
                        return callback(error);
                    }

                    const params = {
                        Message: `Could not scan ${bucket}/${file} : ${error.message}`,
                        TopicArn: notifyTopicARN
                    };

                    // Create promise and SNS service object
                    var publishTextPromise = new AWS.SNS({ apiVersion: '2010-03-31' }).publish(params).promise();

                    // Handle promise's fulfilled/rejected states
                    publishTextPromise.then(function(data) {
                        console.log(`Message ${params.Message} sent to the topic ${params.TopicArn}`);
                        console.log("MessageID is " + data.MessageId);
                        callback(error);
                    }).catch(function(err) {
                        console.log(`Unable to send message`);
                        console.error(err);
                        callback(error);
                    });
                });
        },

        tagSet: function(callback) {
            var params = {
                Bucket: bucket,
                Key: file
            };
            s3.getObjectTagging(params)
                .promise()
                .then(function(data) {
                    if (!data || !data.TagSet || !Array.isArray(data.TagSet)) {
                        console.log('invalid response', data);
                        return callback(new Error('Invalid getObjectTagging response', data));
                    }
                    console.log('tagSet', data.TagSet);
                    callback(null, data.TagSet);
                })
                .catch(function(error) {
                    // Error 😨
                    if (error.response) {
                        /*
                         * The request was made and the server responded with a
                         * status code that falls out of the range of 2xx
                         */
                        console.log('response data:', error.response.data);
                        console.log('response status:', error.response.status);
                        console.log('response headers:', error.response.headers);
                    } else if (error.request) {
                        /*
                         * The request was made but no response was received, `error.request`
                         * is an instance of XMLHttpRequest in the browser and an instance
                         * of http.ClientRequest in Node.js
                         */
                        console.log('response request:', error.request);
                    } else {
                        // Something happened in setting up the request and triggered an Error
                        console.log('Error (unknown):', error.message);
                    }
                    console.log('config:', error.config);
                    callback(null);
                });
        },

        infected: ['scan', function(data, callback) {
            const body = data.scan && data.scan.body;
            const infected = body && body.Infected;
            if (typeof infected === 'undefined') {
                // Scan Failure - do not delete the file!
                console.log('Failed?', body, body && body.Infected, infected);
                return callback(null, null);
            }
            console.log('Infected = ' + infected);
            return callback(null, infected);
        }],

        tagged: ['infected', 'tagSet', function(data, callback) {
            console.log('tag?');
            if (!data.scan || data.infected) {
                console.log('nope!');
                return callback(null); // don't tag if we are deleting it
            }

            const failed = (data.infected === null);

            var tagged = false;
            const tagSet = data.tagSet.map(function(tag) {
                if (tag.Key === 'Scanned') {
                    tag.Value = failed ? "failed" : "clean";
                    tagged = true;
                }
                return tag;
            });
            if (!tagged) {
                tagSet.push({
                    Key: "Scanned",
                    Value: failed ? "failed" : "clean"
                });
            }

            var params = {
                Bucket: bucket,
                Key: file,
                Tagging: {
                    TagSet: tagSet
                }
            };
            s3.putObjectTagging(params)
                .promise()
                .then(function(data) {
                    console.log('tagging complete', data);
                    callback(null);
                })
                .catch(function(err) {
                    console.error(err);
                    return callback(err);
                });

        }],

        deleted: ['infected', function(data, callback) {
            console.log('delete?');
            if (!data.scan || !data.infected) {
                console.log('nope!');
                return callback(null);
            }

            const infection = data.scan.body.InfectionName || '<unknown>';

            console.log('deleting file ', bucket, file, ' infected with ', infection);

            var params = {
                Bucket: bucket,
                Key: file
            };

            s3.deleteObject(params, function(err, data) {
                if (err) {
                    console.log(err, err.stack);
                } else {
                    console.log('successfully deleted', bucket, file);
                }
                // return the results of the deletion
                callback(null, {
                    err: err,
                    data: data,
                    infection: infection
                });
            });
        }],

        notify: ['deleted', function(data, callback) {
            // only notify if we deleted it
            console.log('notify?');
            if (!data.deleted) {
                console.log('nope!');
                return callback(null);
            }

            const infection = data.scan.body.InfectionName || '<unknown>';

            console.log('deleted file ', bucket, file, ' infected with ', infection);

            if (!notifyTopicARN) {
                return callback(error);
            }

            const params = {
                Message: `Could not scan ${bucket} / ${file} : ${error.message}`,
                TopicArn: notifyTopicARN
            };

            // Create promise and SNS service object
            var publishTextPromise = new AWS.SNS({ apiVersion: '2010-03-31' }).publish(params).promise();

            // Handle promise's fulfilled/rejected states
            publishTextPromise.then(function(data) {
                console.log(`Message ${params.Message} sent to the topic ${params.TopicArn}`);
                console.log("MessageID is " + data.MessageId);
                callback(null);
            }).catch(function(err) {
                console.error(err, err.stack);
                callback(null);
            });

        }]

    }, function(err, result) {
        console.log('handleRecord', {err: err, result: result});
        callback(err, result);
    });
}

//
// lambda function entry point
//
exports.sqsPayloadLoggerHandler = async (event, context, callback) => {
    // All log statements are written to CloudWatch by default. For more information, see
    // https://docs.aws.amazon.com/lambda/latest/dg/nodejs-prog-model-logging.html
    console.log(event);
    if (!scanServer || !process.env.S3Key || !process.env.S3Secret) {
        console.log('Check environment variables are set:', { scanServer : 'eg 10.0.0.58', S3Key: 's3 IAM user key', S3Secret: 's3 IAM user secret'});
    }

    const s3 = new AWS.S3({
        accessKeyId: process.env.S3Key,
        secretAccessKey: process.env.S3Secret
    });

    return new Promise((resolve, reject) => {
        async.each(event.Records, function (record, callback) {
            console.log('Parsing', record);

            const body = record.body && JSON.parse(record.body);
            const records = body && body["Records"];

            if (!records) {
                console.log('No body, parsing individual record', record.body, JSON.stringify({body: record.body}), body);
                return handleRecord(s3, record, callback);
            }

            if (!Array.isArray(records)) {
                console.log('"Records" is not an array. Failing.', records);
                return callback(null); // silent failure
            }

            // array may have more than one file?
            async.each(records, function(record, callback) {
                handleRecord(s3, record, callback);
            }, function(err, result) {
                console.log('async.each', {err: err, result: result});
                callback(err, result);
            });

        }, function (error, output) {
            console.log('Completed', error, output);
            if (error) {
                return reject(error);
            }
            resolve(null, 200); //output);
        });
    });
};
