# PersonDetector

Main purpose of this application is :
- According to MQTT signal start catching frames from rtsp camera stream
- Send images to AWS Rekognito and chek if image contain person
- If image contain persom send email to certain email address and store image to AWS S3


settings.json
{

    // MQTT Server IP Adress e.g. 192.168.50.228  
    "mqttServer": "",
    
    // MQTT Server Port e.g. 1883
    "mqttPort": , 
    
    // MQTT Client name 
    "mqttCredentialsName": "",
    
    // Internal buffer max. size
    "bufferLimit": 40,
    
    // MQTT Client password
    "mqttCredentialsPassword": "",
    
    // Mane of AWS S3 bucket
    "S3Bucket": "",
  
    // "MQTT Topic" : "rtsp stream" e.g 
    //"streams": {
    //   "balcony/cameraMotion": "rtsp://user:password@192.168.50.215:554/stream1",
    //  "garage/motionDetector": "rtsp://user:password@192.168.50.12:554/stream1"
    //}
    "streams": {
       "balcony/cameraMotion": "rtsp://user:password@192.168.50.215:554/stream1",
      "garage/motionDetector": "rtsp://user:password@192.168.50.12:554/stream1"
    }
  
}
