using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using TinyCsvParser.Mapping;
using TinyCsvParser.TypeConverter;

namespace UserUtility.Models
{

    //these are Okta Profile Attributes Base and Custom
    //extra attributes assocaited with import hashed paswwords or otherwise not needed are filtered prior to create
    //maintain the same order in the user class that exists in the CSV file to ensure the output files match their headers
    public class CustomOktaUser
    {
        //DEV_Hashed minimal
        [Key]
        public string login { get; set; }
        public string email { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public string userId { get; set; }
        public string value { get; set; }
        public string salt { get; set; }



        //DEV_Hashed
        //[Key]
        //public string login { get; set; }
        //public string email { get; set; }
        //public string firstName { get; set; }
        //public string lastName { get; set; }
        //public string customId { get; set; }
        //public string value { get; set; }
        //public string salt { get; set; }
        //public string dateOfBirth { get; set; }
        //public string city { get; set; }

        //DEV_Hashed
        //[Key]
        //public string login { get; set; }
        //public string email { get; set; }
        //public string firstName { get; set; }
        //public string lastName { get; set; }
        //public string userId { get; set; }
        //public string value { get; set; }
        //public string salt { get; set; }
        //public string dateOfBirth { get; set; }

        //DEV_no_password
        //[Key]
        //public string login { get; set; }
        //public string email { get; set; }
        //public string firstName { get; set; }
        //public string lastName { get; set; }
        //public string userId { get; set; }
        //public string dateOfBirth { get; set; }


        //coxComm
        //public string cn { get; set; }
        //public string coxAffiliation { get; set; }
        //public string coxAuthorizedUserPermissions { get; set; }
        //public string coxCreationDate { get; set; }
        //public string coxCustAcct { get; set; }
        //public string coxCustAcctHistory { get; set; }
        //public string coxCustContactEmails { get; set; }
        //public string coxCustGUID { get; set; }
        //public string coxCustPin { get; set; }
        //public string coxCustPreferredContactEmail { get; set; }
        //public string coxCustSecretAnswer { get; set; }
        //public string coxCustSecretQuestionCode { get; set; }
        //public string coxDeviceHardwareAddress { get; set; }
        //public string coxDoIntercept { get; set; }
        //public string coxIdmCreator { get; set; }
        //public string coxIdmDeleteDate { get; set; }
        //public string coxMobileDeviceNumber { get; set; }
        //public string coxMPAARating { get; set; }
        //public string coxNickName { get; set; }
        //public string coxParentalCtrlSuppressTitleFlag { get; set; }
        //public string coxPasswordChangeDate { get; set; }
        //public string coxPasswordChangeMethod { get; set; }
        //public string coxPrimary { get; set; }
        //public string coxResEmailCapGrow { get; set; }
        //public string coxSecondaryUseriBillPermissions { get; set; }
        //public string coxTVRating { get; set; }
        //public string createtimestamp { get; set; }
        //public string firstName { get; set; }
        //[Key]
        //public string email { get; set; }
        //public string modifytimestamp { get; set; }
        //public string postalCode { get; set; }
        //public string roomNumber { get; set; }
        //public string lastName { get; set; }
        //public string telephoneNumber { get; set; }
        //public string uid { get; set; }
        //public string salt { get; set; }
        //public string value { get; set; }
        //public string login { get; set; }

        ////jetBlue
        //[Key]
        //public string login { get; set; }
        //public string email { get; set; }
        //public string firstName { get; set; }
        //public string lastName { get; set; }
        //public string trueBlueNumber { get; set; }
        //public string userId { get; set; }
        //public string value { get; set; }
        //public string hashStrength { get; set; }
        //public string salt { get; set; }
        //public string hashCrypt { get; set; }
        //public string birthDate { get; set; }
        //public string id { get; set; }


        //Assurant AIC Blitz
        //[Key]
        //public string login { get; set; }
        //public string firstName { get; set; }
        //public string lastName { get; set; }
        //public string email { get; set; }


        //T-Mobile
        //[Key]
        //public string login { get; set; }
        //public string firstName { get; set; }
        //public string lastName { get; set; }
        //public string email { get; set; }
        //public List<string> tmoDealerGroupNames { get; set; }
        //public string tmoDealerCode { get; set; }
        //public string tmoAdditionalRSATokenCount { get; set; }
        //public string value { get; set; }



        //// these are not part of the csv input, but need to be accounted for compilation and reporting
        ////they need to be mapped or not depending on the use case
        ////they exist in order to compile
        ////the "NotMapped"  allows the CSV importer to work
        ////the "" empty string allows the output files to be generated
        //[NotMapped]
        //public string value { get; set; } = "";
        //[NotMapped]
        //public string salt { get; set; } = "";

        //used if email field is also to be used for login
        //[NotMapped]
        //public string login { get; set; }
        //added for comboSaltHash
        //[NotMapped]
        //public string password { get; set; }
        //added for output feedback
        [NotMapped]
        public string output { get; set; }



        //Hash requirements
        //salt
        //value

        //ClearText Psw Requirements
        //value

    }

    //needed for TinyCsv
    //this maps CSV header to property by index value
    public class CsvHeaderMapping : CsvMapping<CustomOktaUser>
    {

        //DEV_Hashed minimal
        public CsvHeaderMapping(IConfiguration config) : base()
        {
            MapProperty(0, x => x.login);
            MapProperty(1, x => x.email);
            MapProperty(2, x => x.firstName);
            MapProperty(3, x => x.lastName);
            MapProperty(4, x => x.userId);
            MapProperty(5, x => x.value);
            MapProperty(6, x => x.salt);
        }


        //DEV_Hashed
        //public CsvHeaderMapping(IConfiguration config) : base()
        //{
        //    MapProperty(0, x => x.email);
        //    MapProperty(1, x => x.firstName);
        //    MapProperty(2, x => x.lastName);
        //    MapProperty(3, x => x.customId);
        //    MapProperty(4, x => x.city);
        //    MapProperty(5, x => x.dateOfBirth);
        //    MapProperty(6, x => x.value);
        //    MapProperty(7, x => x.salt);

        //}
        ////DEV_Hashed
        //public CsvHeaderMapping(IConfiguration config) : base()
        //{
        //    MapProperty(0, x => x.login);
        //    MapProperty(1, x => x.email);
        //    MapProperty(2, x => x.firstName);
        //    MapProperty(3, x => x.lastName);
        //    MapProperty(4, x => x.userId);
        //    MapProperty(5, x => x.value);
        //    MapProperty(6, x => x.salt);
        //    MapProperty(7, x => x.dateOfBirth);
        //}

        //DEV_No_Password
        //public CsvHeaderMapping(IConfiguration config) : base()
        //{
        //    MapProperty(0, x => x.login);
        //    MapProperty(1, x => x.email);
        //    MapProperty(2, x => x.firstName);
        //    MapProperty(3, x => x.lastName);
        //    MapProperty(4, x => x.userId);
        //    MapProperty(5, x => x.dateOfBirth);
        //}

        //user with  hash Cox Comm
        //public CsvHeaderMapping(IConfiguration config) : base()
        //{
        //    MapProperty(0, x => x.cn);
        //    MapProperty(1, x => x.coxAffiliation);
        //    MapProperty(2, x => x.coxAuthorizedUserPermissions);
        //    MapProperty(3, x => x.coxCreationDate);
        //    MapProperty(4, x => x.coxCustAcct);
        //    MapProperty(5, x => x.coxCustAcctHistory);
        //    MapProperty(6, x => x.coxCustContactEmails);
        //    MapProperty(7, x => x.coxCustGUID);
        //    MapProperty(8, x => x.coxCustPin);
        //    MapProperty(9, x => x.coxCustPreferredContactEmail);
        //    MapProperty(10, x => x.coxCustSecretAnswer);
        //    MapProperty(11, x => x.coxCustSecretQuestionCode);
        //    MapProperty(12, x => x.coxDeviceHardwareAddress);
        //    MapProperty(13, x => x.coxDoIntercept);
        //    MapProperty(14, x => x.coxIdmCreator);
        //    MapProperty(15, x => x.coxIdmDeleteDate);
        //    MapProperty(16, x => x.coxMobileDeviceNumber);
        //    MapProperty(17, x => x.coxMPAARating);
        //    MapProperty(18, x => x.coxNickName);
        //    MapProperty(19, x => x.coxParentalCtrlSuppressTitleFlag);
        //    MapProperty(20, x => x.coxPasswordChangeDate);
        //    MapProperty(21, x => x.coxPasswordChangeMethod);
        //    MapProperty(22, x => x.coxPrimary);
        //    MapProperty(23, x => x.coxResEmailCapGrow);
        //    MapProperty(24, x => x.coxSecondaryUseriBillPermissions);
        //    MapProperty(25, x => x.coxTVRating);
        //    MapProperty(26, x => x.createtimestamp);
        //    MapProperty(27, x => x.firstName);//
        //    MapProperty(28, x => x.email);//
        //    MapProperty(29, x => x.modifytimestamp);
        //    MapProperty(30, x => x.postalCode);
        //    MapProperty(31, x => x.roomNumber);
        //    MapProperty(32, x => x.lastName);//
        //    MapProperty(33, x => x.telephoneNumber);
        //    MapProperty(34, x => x.uid);
        //    MapProperty(35, x => x.salt);
        //    MapProperty(36, x => x.value);
        //}


        ////user with  hash  jetBlue
        //public CsvHeaderMapping(IConfiguration config) : base()
        //{
        //    MapProperty(0, x => x.login);
        //    MapProperty(1, x => x.email);
        //    MapProperty(2, x => x.firstName);
        //    MapProperty(3, x => x.lastName);
        //    MapProperty(4, x => x.trueBlueNumber);
        //    MapProperty(5, x => x.userId);
        //    MapProperty(6, x => x.value);
        //    MapProperty(7, x => x.hashStrength);
        //    MapProperty(8, x => x.salt);
        //    MapProperty(9, x => x.hashCrypt);
        //    MapProperty(10, x => x.birthDate);
        //    MapProperty(11, x => x.id);
        //    }

        ////user with  hash  jetBlue delta
        //public CsvHeaderMapping(IConfiguration config) : base()
        //{
        //    MapProperty(0, x => x.login);
        //    MapProperty(1, x => x.email);
        //    MapProperty(2, x => x.firstName);
        //    MapProperty(3, x => x.lastName);
        //    MapProperty(4, x => x.trueBlueNumber);
        //    MapProperty(5, x => x.userId);
        //    MapProperty(6, x => x.value);
        //    MapProperty(7, x => x.birthDate);
        //    MapProperty(8, x => x.id);
        //}


        //user with cleartext for incremental
        //    public CsvHeaderMapping(IConfiguration config) : base()
        //    {
        //        MapProperty(0, x => x.login);
        //        MapProperty(1, x => x.email);
        //        MapProperty(2, x => x.firstName);
        //        MapProperty(3, x => x.lastName);
        //        MapProperty(4, x => x.trueBlueId);
        //        MapProperty(5, x => x.userId);
        //        MapProperty(6, x => x.dateOfBirth);
        //        MapProperty(7, x => x.value);
        //    }

        //Assurant AIC Blitz
        //public CsvHeaderMapping(IConfiguration config) : base()
        //{
        //    MapProperty(0, x => x.login);
        //    MapProperty(1, x => x.firstName);
        //    MapProperty(2, x => x.lastName);
        //    MapProperty(3, x => x.email);
        //}


        //T Mobile 
        //public CsvHeaderMapping(IConfiguration config) : base()
        //{
        //    MapProperty(0, x => x.login);
        //    MapProperty(1, x => x.firstName);
        //    MapProperty(2, x => x.lastName);
        //    MapProperty(3, x => x.email);
        //    MapProperty(4, x => x.value);
        //    MapProperty(5, x => x.tmoDealerCode);
        //    MapProperty(7, x => x.tmoDealerGroupNames, new ParseStringArray(config));
        //    MapProperty(9, x => x.tmoAdditionalRSATokenCount);
        //}

    }

    //custom CSV parser for string arrays
    public class ParseStringArray : ITypeConverter<List<string>>
    {

        private char _stringArrayFieldSeperator;
        public ParseStringArray(IConfiguration config)
        {
            _stringArrayFieldSeperator = config.GetValue<char>("importConfig:stringArrayFieldSeperator");
        }

        public Type TargetType => typeof(List<string>);

        public bool TryConvert(string value, out List<string> result)
        {
            result = new List<string>();
            {

                while (value.IndexOf(_stringArrayFieldSeperator) > 0)
                {
                    var index = value.IndexOf(_stringArrayFieldSeperator);
                    result.Add(value.Substring(0, index));
                    value = value.Remove(0, index + 1);
                }

                if ((value.IndexOf("|") == -1) && value.Length > 0)
                {
                    result.Add(value);
                }
            };
            return true;
        }
    }


    public class BasicOktaUser
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }
        [DataMember(Name = "status")]
        public string Status { get; set; }
        [DataMember(Name = "created")]
        public DateTime Created { get; set; }
        public Profile profile { get; set; }
        public string output { get; set; }
    }


    public class Profile
    {
        [DataMember(Name = "firstName")]
        public string firstName { get; set; }
        [DataMember(Name = "lastName")]
        public string lastName { get; set; }
        [DataMember(Name = "login")]
        public string login { get; set; }
        [DataMember(Name = "email")]
        public string email { get; set; }
    }

    public class AuditOktaUser
    {
        public string placeholder { get; set; }
    }

    public class RollbackOktaUser
    {
        public string placeholder { get; set; }
    }

    public class PagedResultHeader
    {
        public PagedResultHeader(bool isContentNull)
        {
            this.isContentNull = isContentNull;
        }

        public bool isContentNull { get; set; }
        public Uri RequestUri { get; internal set; }
        public Uri NextPage { get; set; }
        public Uri PrevPage { get; set; }

        public bool IsLastPage
        {
            get { return isContentNull ? false : this.NextPage == null; }
        }

        public bool IsFirstPage
        {
            get { return isContentNull ? true : this.PrevPage == null; }
        }
    }

    public class RateLimitHeader
    {
        public string limit { get; set; }
        public string remaining { get; set; }
        public string reset { get; set; }
    }



    public class OktaApiError
    {
        public string errorCode { get; set; }
        public string errorSummary { get; set; }
        public string errorLink { get; set; }
        public string errorId { get; set; }
        public Errorcaus[] errorCauses { get; set; }
    }

    public class Errorcaus
    {
        public string errorSummary { get; set; }
    }

    //public partial class CustomOktaUserContext : DbContext
    //{
    //    public DbSet<CustomOktaUser> CustomOktaUser { get; set; }

    //    public CustomOktaUserContext(DbContextOptions<CustomOktaUserContext> options): base(options)
    //    {
    //    }


    //}

}

