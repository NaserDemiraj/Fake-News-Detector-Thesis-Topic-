@echo off
echo Setting up Fake News Detector project...

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
echo Start the backend:  cd backend  ^&^& dotnet run    (http://localhost:5000)
echo Start the frontend: cd frontend ^&^& npm run dev   (http://localhost:3000)
