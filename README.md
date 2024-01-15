# omniATE
Automated Test Environment - Factory Acceptance Testing Tool

## Description

The Omnitronics bootloader should be able to be read from the COM ports using this tool, the API will read the bootloader info into MongoDB for NoSQL storage. 

These `documents` will be listed on the UI for generating the report, and thus being able to be generated into an A4 or A5 report for printing in a standard format.

The generated output can be used to validate the operational status of any Omnitronics device that utilises the serial port for output of boot information.

## Environments

**develop**

This branch exists for the development of features for the ATE tool.

**main**

This branch exists as the current production-ready instance.