# Docker Hub Publishing Guide

This guide explains how to publish Optimarr to Docker Hub.

## Prerequisites

1. A Docker Hub account ([sign up here](https://hub.docker.com/signup))
2. Docker installed on your machine
3. The Dockerfile and related files in this repository

## Step 1: Update Dockerfile Labels

Before pushing, update the labels in `Dockerfile` with your actual repository information:

```dockerfile
LABEL org.opencontainers.image.url="https://github.com/yourusername/optimarr"
LABEL org.opencontainers.image.documentation="https://github.com/yourusername/optimarr/blob/main/README.md"
LABEL org.opencontainers.image.source="https://github.com/yourusername/optimarr"
```

Replace `yourusername` with your actual GitHub/Docker Hub username.

## Step 2: Build the Image

Build the Docker image:

```bash
docker build -t yourusername/optimarr:latest .
```

Replace `yourusername` with your Docker Hub username.

## Step 3: Test the Image Locally

Before pushing, test the image locally:

```bash
docker run -d \
  --name optimarr-test \
  -p 5000:8080 \
  yourusername/optimarr:latest
```

Verify it works by visiting `http://localhost:5000` and checking the health endpoint:
```bash
curl http://localhost:5000/api/system/health
```

Stop and remove the test container:
```bash
docker stop optimarr-test
docker rm optimarr-test
```

## Step 4: Tag the Image

Tag the image with version numbers:

```bash
# Tag as latest
docker tag yourusername/optimarr:latest yourusername/optimarr:latest

# Tag with version number (update version as needed)
docker tag yourusername/optimarr:latest yourusername/optimarr:0.0.1
```

## Step 5: Login to Docker Hub

```bash
docker login
```

Enter your Docker Hub username and password when prompted.

## Step 6: Push to Docker Hub

Push both tags:

```bash
# Push latest tag
docker push yourusername/optimarr:latest

# Push version tag
docker push yourusername/optimarr:0.0.1
```

## Step 7: Update Documentation

After pushing, update the following files with your Docker Hub username:

1. **README.md**: Replace `yourusername/optimarr` with your actual Docker Hub repository name
2. **docker-compose.hub.yml**: Update the image name
3. **docker-compose.yml**: Update the commented image line

## Step 8: Configure Docker Hub Repository

On Docker Hub, configure your repository:

1. Go to your repository settings
2. Set the repository description
3. Add tags/readme if desired
4. Configure build settings (optional - for automated builds)

## Automated Builds (Optional)

You can set up automated builds on Docker Hub:

1. Go to your repository on Docker Hub
2. Navigate to "Builds" tab
3. Connect your GitHub repository
4. Configure build rules:
   - Source: `main` branch
   - Dockerfile location: `/Dockerfile`
   - Tag: `latest`

This will automatically build and push new images when you push to your GitHub repository.

## Versioning Strategy

Recommended versioning approach:

- `latest`: Always points to the most recent stable release
- `0.0.1`, `0.0.2`, etc.: Specific version tags
- `0.0.1-beta`: Pre-release versions

Update the version in `optimarr.csproj` and rebuild/push when releasing new versions.

## Troubleshooting

### Authentication Issues
```bash
# Logout and login again
docker logout
docker login
```

### Push Permission Denied
- Ensure you're logged in with `docker login`
- Verify the repository name matches your Docker Hub username
- Check that the repository exists on Docker Hub (create it if needed)

### Build Fails
- Check Dockerfile syntax
- Ensure all required files are present
- Review build logs for specific errors

## Next Steps

After publishing:

1. Share the Docker Hub repository link with users
2. Update your project's main README with pull instructions
3. Consider adding badges to your README showing Docker Hub image size, pulls, etc.

