#!/bin/bash

# Setup script for Fake News Detector project

echo "Setting up Fake News Detector project..."

# Install root dependencies
npm install

# Setup backend
echo "Setting up backend..."
cd backend
dotnet restore
echo "Backend setup complete!"

# Setup frontend
echo "Setting up frontend..."
cd ../frontend
npm install
echo "Frontend setup complete!"

cd ..
echo "Setup complete! You can now run the project."
echo "To start both frontend and backend: npm start"
echo "To start the frontend only: npm run frontend"
echo "To start the backend only: npm run backend"
