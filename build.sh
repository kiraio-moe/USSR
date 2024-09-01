#!/bin/bash

VERSION=$(<version.txt)

echo "Building USSR v${VERSION}..."
# Define the target operating systems and architectures
TARGET_OS_ARCHITECTURES=("win-x64" "win-arm64" "linux-x64" "linux-arm64" "osx-x64" "osx-arm64")
TARGET_FRAMEWORKS=("net6.0" "net8.0")

# Build the project for each target OS architecture
for os_arch in "${TARGET_OS_ARCHITECTURES[@]}"
do
    for framework in "${TARGET_FRAMEWORKS[@]}"
    do
        echo "Building for $os_arch architecture..."
        dotnet publish -c Release -f "$framework" -r "$os_arch" --no-self-contained

        # Check if build was successful
        if [ $? -eq 0 ]; then
            echo "Build for $os_arch completed successfully."
        else
            echo "Build for $os_arch failed."
            exit 1  # Exit the script with an error code
        fi
    done
done

echo "Build process completed for all target OS architectures."

echo "Distributing classdata.tpk to every architecture..."
for framework in "${TARGET_FRAMEWORKS[@]}"
do
    for os_arch in "${TARGET_OS_ARCHITECTURES[@]}"
    do
        PUBLISH_PATH="bin/Release/${framework}/${os_arch}/publish"
        ZIP_OUTPUT="USSR-v${VERSION}-${framework}-${os_arch}.zip"

        # Copy classdata.tpk to every architecture
        mkdir -p "${PUBLISH_PATH}"
        cp "classdata.tpk" "${PUBLISH_PATH}"

        # Copy version.txt
        cp "version.txt" "${PUBLISH_PATH}"

        # Make a zip file for every architecture to be distributed
        cd "${PUBLISH_PATH}"
        zip -r "../../../${ZIP_OUTPUT}" *
        cd "../../../../../" # build.sh directory
    done
done
