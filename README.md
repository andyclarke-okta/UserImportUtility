# DataMigration_UserUtility.

**NEW**
- **High Performance Test User Load**

**Implements Data Migration Tasks:** 
- **High Performance Bulk User Import**     
- **Delete Users**    
- **Validate Users**
- **Obfuscate Users**      
[Data Migration wiki](https://oktawiki.atlassian.net/wiki/spaces/serv/pages/643860141/Technical%2BDomain%2BData%2BMigration)



## System Requirements 
This application was built using Visual Studio 2019 Community Edition with dotNet Core 3.1.     
dotNet Core can be executed on any platform.
* implements runtime very similiar to Java 
* Windows
* Mac
* Linux

Check here for runtime environments for all OSs.     
[download runtime](https://dotnet.microsoft.com/download/dotnet-core/2.2)


## Features 
* Test User Loader create GUID named test users 
	* Using multiple tasks (configurable)to add user as fast as rate limits allow
	* No logs or output files are produced
	* email == login


* Extends Producer Consumer Model to Pipeline model
	* Multiple Queues and Multiple Stages can be linked together
* All Pipeline Use Cases are constructed using Dependency Injection
* Pipelines can be executed
	* Concurrently, start processing events while queue is still filling
	* Sequentially, wait until queue is filled before processing
* All executions are performed on Asynchronous Tasks
	* Threading is handled efficiently at OS level
	* Uses OS Thread Pools instead of manually creating threads
	* No knowledge of underlying hardware is required
* Configurable number of Service Queue(s) spawned as Tasks
	* Each Service Queue extracts from queue concurrently and initiates Asynchronous actions ( APIs)
	* Can send very large amount of APIs very quickly
* Configurable Throttling mechanisms to control Rate Limit
* Most features are configured from JSON file without need for re-compiling
* Data Sources
	* CSV File
	* API ( for example; Okta GetUsersByGroup)
	* DataBase
* Use Cases Supported
	* Import (Create Okta Users)
		* Password Hash
		* No Password (STAGED or PROVISIONED)
		* Add to Group
		* Load Custom Okta Profile
	* Rollback (Delete Okta User)
	* Audit ( read all users in group/tenant with status)
	* Incremental ( Create/Update Okta Users)
		* Password updated
		* Profile updated
		* Create user without password
		* Create user with cleartext password
	* Validate
		* check list of attributes for null or empty
		* check list of attributes for email format
		* check list of attributes for maximum length
		* check list of attributes for forbidden characters
	* Obfuscate Users
* Multiple Output CSV Files
	* Success -for Audit
	* Failure - debug anomolies
	* Replay - 429 errors, can be recylced as in input file
* Multi-Tiered, Multi-Output Logging
	* writes log to console
	* write log to file
	* Tiered
	* Trace
		* for TC to integrate new use case
		* multiple entries per user
		* very large amount of data
		* can figure out most anything
	* Debug
		* per user entry output
	* Info
		* suitable for production
		* high level processes only
		* displays summary data
	*Error
		* should not see anything!

##  Architectural Diagram 

*link to wiki design doc*

## Build
The DataMigration_UserUtility can be complied from Visual Studio, Visual Studio Code or command line. To build the application from the command line;            
cd to the solution root directory ( where UserUtility.sln is located )    
>dotnet clean   
	* deletes binary and workign files from previous compilations   
>dotnet build   
	* created UserUtility.dll  
	* useful if need to add custom Okta attributes to the import  
	* most other modifications can be made with JSON config files and not recompilation     
>dotnet publish      
	* gathers all necessary files for execution on ANY platform into 'publish' folder      
	* see ..\bin\Debug\netcoreapp2.2\publish      
	
## Execution
Note: Some of the usage strategies are being developed. Check back for further updates

The application is executed from the command line on any OS.
After building executables, navigate to 'publish' folder in command window.
Execute UserUtility.dll (see sample commands below)
For convienence and testing, two folders appear at the top level
* dataMigration_Import
	* create users in Okta from CSV file
	* Sample-nonMappedValues_1000.csv is provded for test data
* dataMigration_Rollback
	* deactivate/delete users from Okta
* Download and extract zip file 
* cd to the folder
* Change the Org, apiToken and the groupId to match your test Okta tenant

Command for dataMigration_Rollback;
>dotnet UserUtility.dll

Command for dataMigration_Import with test CSV file
>dotnet UserUtility.dll --inputFile Sample_nonMappedValues_1000.csv
where --inputFile is the source file when using the CSV option

## Configuration
All the run time parameters are read into the application during initialzation. These parameters can be updated without recompiling code. Use your favorit editor to make adjustments.

Config files are;
* generalConfig.json
	* universal config for all workflows

* nlog.config
	* Set logging level and output targets


## Usage 

The application makes use of JSON configuration files to set runtime parameters. 

```json
User General Configuration Example;
{
  "generalConfig": {
    "org": "https://subdomain.okta.com",
    "apiToken": "00fEy6uHAbOvfqJdic-xxxxxxxxxxxxxx",
    "isValidation": false,
    "outputQueueBufferSize": 20000000,
    "userQueueBufferSize": 20000000,
    "producerTasks": 5,
    "consumerTasks": 1,
    "queueWaitms": 3000,
	"reportingWaitms":  3000,  //3000;increase when too many Error Adding Queue Entry
    "throttleMs": 100
  },
    "testUserConfig": {
    "testUserDomain": "myDomain.com",
    "testUserPsw": "Password@1",
    "numTestUsers": 500
  },
  "importConfig": {
    "inputFileFieldSeperator": ",",
	"stringArrayFieldSeperator": "|",
    "groupId": "00gbmydpimCv4zoJ81t7",
	"isCustomInputLogic": false, //use this code block to implement customer specific transformations of input fields
    "isLoginEqualEmail": false,
	"isComboHashSalt": true,
    "activateUserwithCreate": "false",
    "provisionUser": true, //will activate user when no password supplied
    "sendEmailwithActivation": "false", //emails never sent when password included
    "workFactor": 10, //BRCYPT only
    "algorithm": "NONE", // BCRYPT,SHA-1, SHA-256,SHA-512,MD5, CLEARTEXT,NONE
    "saltOrder": "POSTFIX", //PREFIX, POSTFIX (SHA-x, MD5 only)
    "omitFromUserProfile": [
      "value",
      "salt"
    ]
  },
  "rollbackConfig": {
    "deactivateOnly": false,
    "secondDeleteDelayMs": 50,
    "excludeOktaIds": [
    ]
  },
  "userApiConfig": { //for audit and rollback features
    "apiPageSize": 200,
    "endpoint": "groups", //groups, users
    "groupId": "00gjvaqcb3PzYuKmz0h7"
  },
  "validationConfig": {
    "validateEmailFormat": [
      "email",
      "login"
    ],
    "validateNull": [
      "employeeNumber"
    ],
    "validateUniqueness": [
      "email"
    ],
    "validateFieldLength": [
      "trueBlueId:10"
    ]
  },
  "obfuscateConfig": {
    "obfuscateWithStatic": [
      "firstName:somedata",
      "lastName:somedata"
    ],
    "obfuscateWithGUID": [
      "email"
    ]
  }
}

```

- **org:** The base URL for your Okta organization
- **apiToken:** apiToken with proper scope
- **isValidation:** true/false create parallel producer queue (only needed for uniqueness validation)
- **outputQueueBufferSize:** Size of Output BlockingQueueS; Success, Failure, Replay 
- **userQueueBufferSize:**
- **producerTasks:** Number of Task for Parallel inout (CSV only)
- **consumerTasks:** Number of Service Queues initiating worker tasks
- **queueWaitms:**  wait to wait when queue is empty
- **reportingWaitms:** One time pause at end of execution to gather summary information
- **throttleMs:** Throttle Service Queue to avoid Rate Limits

- **testUserDomain:** email domain of test user
- **testUserPsw:** password for all test users
- **numTestUsers:** number of test user to create in Okta Org

- **inputFileFieldSeperator:** field delimiter in input file
- **stringArrayFieldSeperator: ** field delimiter for string arrays embedded into input file
- **groupId:** Add User to Group
- **isCustomInputLogic:** true/false use this code block to implement customer specific transformations of input fields
- **isLoginEqualEmail:** true/false, use single column for both username and email
- **isComboHashSalt:** true/false, use custom processing to decipher salt and hash data
- **activateUserwithCreate:** true/false
- **provisionUser:** true/false; will activate user when no password supplied
- **sendEmailwithActivation:** true/false; emails never sent when password included
- **activateUser:** true/false activate user on create
- **workFactor:** only needed for BCRYPT hash
- **algorithm:** specify password type
- **saltOrder:** only needed for SHA-x and MD5 hash
- **omitFromUserProfile:** Array of input attributes NOT to be included in user profile

NOTE: "isLoginEqualEmail" and "isComboHashSalt" are flags to invoke transformation of input fields prior to API handling. These can be modified to have custom transformations.

- **deactivateOnly:** true/false user will be deactivated not deleted
- **secondDeleteDelayMs:** delay when linking a deactivation followed by a delete user
- **excludeOktaIds:** Array of user Okta Ids NOT to be deactivated or deleted


- **apiPageSize:** size of page when using GET from Okta
- **endpoint:** users/groups either /api/v1/users  or /api/v1/groups/<groupId>/users
- **groupId:** Get user list from this group (if groups endpoint selected)

- **validateEmailFormat:** Array of attributes to validate email format
- **validateNull:** Array of attributes to valiadte is NOT Null or Empty
- **validateUniqueness:** Array of attributes to check are unique across entire import scope
- **validateFieldLength:** Array of attributes to validate field length \(Format: attributeName : length \)


- **obfuscateWithStatic:** Array of attributes to repalce with static data \(Format: attributeName : staticdata \)
- **obfuscateWithGUID:** Array of attributes to replace with non-repeating data

```xml
Configure Logging Level
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
      autoReload="true"
      internalLogLevel="Info"
      internalLogFile="c:\temp\internal-nlog.txt">

  <!-- the targets to write to -->
  <targets>
    <target xsi:type="File" name="target1" fileName="${basedir}/${level}-${shortdate}.log"
            layout="${date}|${level:uppercase=true}|${message} ${exception}|${logger}|${all-event-properties}" />
    <target xsi:type="Console" name="target2"
        layout="${date}|${level:uppercase=true}|${message} ${exception}" />
  </targets>

  <!-- rules to map from logger name to target -->
  <rules>
    <logger name="*" minlevel="Debug" writeTo="target2,target1" />
    <!--Trace, Debug, Info, Warn, Error and Fatal  -->

  </rules>
</nlog>
```
- **minlevel:** Trace, Debug, Info, Warn, Error and Fatal
- **writeTo:** target2,target1


## Deployment Options ##

* PS Technical Consultant examines Use Cases
	* Obtain small sample data
	* Configure Okta Attributes
	* Configure Group Assignment(s)
* TC creates configured UserUtility(s) deliverable to customer
	* Customer prepares execution environment
	* Customer configures for Hardware
	* Customer configure for negotiated Rate Limit Extension
* Customer Executes Validate User utility
	* TC and Customer develop strategy for data anomalies
* Customer Executes Action User Utility

## Contributing

1. Fork it
2. Create your feature branch (`git checkout -b feature/fooBar`)
3. Commit your changes (`git commit -am 'Add some fooBar'`)
4. Push to the branch (`git push origin feature/fooBar`)
5. Create a new Pull Request

**Note**: Contributing is very similar to the Github contribution process as described in detail 
[here](https://guides.github.com/activities/forking/).

## Contacts


