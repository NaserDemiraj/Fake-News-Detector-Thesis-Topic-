# Fake News Detector

A full-stack application that uses AI and natural language processing to analyze news content and determine its credibility.

## Project Structure

- `frontend/`: Next.js frontend application
- `backend/`: .NET backend API

## Prerequisites

- [Node.js](https://nodejs.org/) (v18 or later)
- [.NET SDK](https://dotnet.microsoft.com/download) (v8.0 or later)
- [Visual Studio Code](https://code.visualstudio.com/)

## Quick Start

1. **Open the project in VS Code**

2. **Install dependencies**:
   \`\`\`
   npm run install-all
   \`\`\`

3. **Start both frontend and backend**:
   - Press F5 and select "Full Stack" from the launch configuration
   - Or run: `npm start`

4. **Access the application**:
   - Frontend: http://localhost:3000
   - Backend API: http://localhost:5000/swagger

## Running Separately

### Frontend

\`\`\`
cd frontend
npm install
npm run dev
\`\`\`

### Backend

\`\`\`
cd backend
dotnet restore
dotnet run
\`\`\`

## Features

- Analyze news articles by URL or text content
- Get detailed credibility scores and analysis factors
- View analysis history and statistics
- Responsive design that works on desktop and mobile
- Fallback mode when backend is unavailable
\`\`\`

```bat file="setup.bat"
@echo off
echo Setting up Fake News Detector project...

rem Install root dependencies
call npm install

rem Setup backend
echo Setting up backend...
cd backend
call dotnet restore
echo Backend setup complete!

rem Setup frontend
echo Setting up frontend...
cd ..\frontend
call npm install
echo Frontend setup complete!

cd ..
echo Setup complete! You can now run the project.
echo To start both frontend and backend: npm start
echo To start the frontend only: npm run frontend
echo To start the backend only: npm run backend
