#!/bin/bash

# Run the consumer app with the correct parameters from environment variables
dotnet MediaUploadService.dll -c $CONSUMER_THREADS -q $MAX_QUEUE_LENGTH -p $STORAGE_PATH