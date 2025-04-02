import socket
import json
import struct
import hashlib
import os
import sys
import time
import threading
import argparse
from concurrent.futures import ThreadPoolExecutor

def calculate_md5(file_path):
    hash_md5 = hashlib.md5()
    with open(file_path, "rb") as f:
        for chunk in iter(lambda: f.read(4096), b""):
            hash_md5.update(chunk)
    return hash_md5.hexdigest()

def upload_file(file_path, thread_id=0, server_address=('localhost', 9000)):
    print(f"Thread {thread_id}: Starting upload of {file_path}")
    
    # Check if file exists
    if not os.path.exists(file_path):
        print(f"Thread {thread_id}: File not found: {file_path}")
        return False
    
    # Get file size and calculate hash
    file_size = os.path.getsize(file_path)
    file_hash = calculate_md5(file_path)
    
    # Add thread_id to filename to make each upload unique
    base_filename = os.path.basename(file_path)
    filename = f"{os.path.splitext(base_filename)[0]}_{thread_id}{os.path.splitext(base_filename)[1]}"
    
    # Prepare metadata
    metadata = {
        "filename": filename,
        "hash": file_hash,
        "size": file_size,
        "timestamp": int(time.time()),
        "title": f"Test Video {thread_id}",
        "description": f"This is a test upload from thread {thread_id}"
    }
    
    # Connect to server
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        start_time = time.time()
        sock.connect(server_address)
        
        # Send metadata size (4 bytes, big-endian)
        metadata_json = json.dumps(metadata).encode('utf-8')
        metadata_size = len(metadata_json)
        sock.sendall(struct.pack('>I', metadata_size))
        
        # Send metadata
        sock.sendall(metadata_json)
        
        # Receive response
        response_data = sock.recv(1024)
        response = json.loads(response_data.decode('utf-8'))
        print(f"Thread {thread_id}: Server response: {response}")
        
        if response.get('status') != 'ok':
            print(f"Thread {thread_id}: Upload rejected by server")
            return False
        
        # Send file data
        with open(file_path, 'rb') as f:
            bytes_sent = 0
            while bytes_sent < file_size:
                chunk = f.read(8192)
                if not chunk:
                    break
                sock.sendall(chunk)
                bytes_sent += len(chunk)
                # Avoid too much console output with many threads
                if bytes_sent % (file_size // 10) == 0 or bytes_sent == file_size:
                    print(f"Thread {thread_id}: Progress: {bytes_sent}/{file_size} bytes ({bytes_sent*100/file_size:.1f}%)")
        
        # Get final response
        final_response_data = sock.recv(1024)
        final_response = json.loads(final_response_data.decode('utf-8'))
        elapsed_time = time.time() - start_time
        print(f"Thread {thread_id}: Final response: {final_response}")
        print(f"Thread {thread_id}: Upload took {elapsed_time:.2f} seconds")
        
        return final_response.get('status') == 'ok'
    
    except Exception as e:
        print(f"Thread {thread_id}: Error: {e}")
        return False
    finally:
        sock.close()

def main():
    parser = argparse.ArgumentParser(description='Test parallel uploads to MediaUploadService')
    parser.add_argument('file_path', help='Path to the file to upload')
    parser.add_argument('-n', '--num-uploads', type=int, default=5, help='Number of parallel uploads')
    parser.add_argument('-p', '--port', type=int, default=9000, help='Server port')
    parser.add_argument('-H', '--host', default='localhost', help='Server hostname')
    args = parser.parse_args()
    
    print(f"Starting {args.num_uploads} parallel uploads of {args.file_path}...")
    
    with ThreadPoolExecutor(max_workers=args.num_uploads) as executor:
        futures = []
        for i in range(args.num_uploads):
            futures.append(executor.submit(upload_file, args.file_path, i, (args.host, args.port)))
        
        # Wait for all uploads to complete
        results = [future.result() for future in futures]
        
        success_count = sum(1 for result in results if result)
        print(f"\nSummary: {success_count}/{args.num_uploads} uploads successful")

if __name__ == "__main__":
    main()