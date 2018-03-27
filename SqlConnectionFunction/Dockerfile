FROM microsoft/azureiotedge-functions-binding:1.0-preview

ENV AzureWebJobsScriptRoot=/app

ARG EXE_DIR=.

COPY $EXE_DIR/ /app

#COPY . /app