#!/bin/bash

# Split the comma-separated list of thread folders
IFS=',' read -ra FOLDERS <<< "$THREAD_FOLDERS"

# Run the producer app with the correct parameters
# The app should be modified to accept multiple folder paths
dotnet ProducerApp.dll "${#FOLDERS[@]}" "$SERVER_HOST" "$SERVER_PORT" "${FOLDERS[@]}"