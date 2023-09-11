# CADHub - Simple CAD Version Control
CADHub is a simple to use Windows Desktop Application that allows you to create projects and easily share CAD files with team members via AWS cloud services. 

## What it consists of

- The client-side application is built with WPF (Windows Presentation Format) using C# and .NET and enables users to add projects and push, fix, fetch and merge changes
- User Authentication using Amazon Cognito 
- The backend consists of AWS Lambda functions behind a AWS API Gateway that creates presigned urls to allow the client application to download and upload objects from and to S3

## Changes in Progress
- Add project sharing features and a full settings page
- Create website to download application (and maybe add some web features)
- Add full version control features
  - See full commit logs
  - Allow for users to rollback to previous states 
