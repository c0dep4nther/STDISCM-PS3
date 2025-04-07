#!/bin/bash

# Get base directory of videos
BASE_DIR="/app/videos"

# Run the producer app with the correct parameters
# The app should be modified to accept multiple folder paths
dotnet ProducerApp.dll "$PRODUCER_THREADS" "$SERVER_HOST" "$SERVER_PORT" "$BASE_DIR"