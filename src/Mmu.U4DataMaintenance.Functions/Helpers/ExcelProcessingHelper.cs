using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Mmu.Common.Api.Service.Interfaces;
using Mmu.Integration.Common.Utilities;
using Mmu.Integration.Common.Utilities.Data.Interfaces;
using Mmu.Integration.Common.Utilities.Management.Interfaces;
using Mmu.U4DataMaintenance.Functions.Models;
using Newtonsoft.Json;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace Mmu.U4DataMaintenance.Functions.Helpers
{
    public class ExcelProcessingHelper
    {
        private readonly ILoggerInjector _loggerProvider;
        private readonly IDataService _dataService;
        private readonly IConfiguration _configuration;
        private HttpClient _httpClient;
        //private readonly ITokenService<TokenInfo> _tokenService;
        private readonly IHttpRequestMessageFactory _messageFactory;
        private readonly EndPointConfigU4 _config;
        public ExcelProcessingHelper(ILoggerInjector loggerProvider,
            IDataService dataService, IConfiguration configuration,
            IHttpRequestMessageFactory messageFactory,
            IHttpClientProvider httpClientProvider,
            //ITokenService<TokenInfo> tokenService,
            IOptions<EndPointConfigU4> options
            )
        {
            _dataService = dataService;
            _loggerProvider = loggerProvider;
            _configuration = configuration;
            _messageFactory = messageFactory;
            _httpClient = httpClientProvider.HttpClient;
            //_tokenService = tokenService;
            _config = options.Value;
        }

        public async Task<IActionResult> ReadFilesFromBlob(string blobName)
        {
            var containerName = "excel";
            //string blobName = "rv.xlsx";
            var azureStorageBlobOptions = new AzureStorageBlobOptions(_configuration);

            //const string folderName = "ExcelUploads";
            var folderPath = Path.Combine(Directory.GetCurrentDirectory());
            //var fileEntries = Directory.GetFiles(folderPath);
            var fileName = folderPath + "\\ExcelFile.xlsx";

            var ms = await azureStorageBlobOptions.GetAsync(containerName, blobName);
            ms.Seek(0, SeekOrigin.Begin);

            //Copy the memoryStream from Blob on to local file
            await using (var fs = new FileStream(fileName ?? throw new InvalidOperationException(), FileMode.OpenOrCreate))
            {
                await ms.CopyToAsync(fs);
                fs.Flush();
            }

            await UpdateExcelForBlobStorageByNameAsync(fileName);

            await using var send = new FileStream(fileName, FileMode.Open, FileAccess.Read);

            await using (var memoryStreamToUpdateBlob = new MemoryStream())
            {
                await send.CopyToAsync(memoryStreamToUpdateBlob);
                memoryStreamToUpdateBlob.Position = 0;
                memoryStreamToUpdateBlob.Seek(0, SeekOrigin.Begin);
                await azureStorageBlobOptions.UpdateFileAsync(memoryStreamToUpdateBlob, containerName, blobName);
            }

            ms.Close();

            return null;
        }

        /// <summary>
        /// Read the rows and update the column data
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public async Task<string> UpdateExcelForBlobStorageByNameAsync(string fileName)
        {
            try
            {
                //TODO: Where to store the sheetnames
                string sheetName = "COT_Data_input_sheet";
                switch (sheetName.ToLower())
                {
                    case "cot_data_input_sheet":
                        await ProcessCoDataInputExcelSheetAsync(fileName, sheetName);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
            }
            return null;
        }

        private async Task ProcessCoDataInputExcelSheetAsync(string fileName, string sheetName)
        {
            var priceGroupQuery = @"Select Id, BusinessMeaningName from BCPriceGroup";
            var courseLevelQuery = @"Select Id, BusinessMeaningName from ACCourselevel";
            var enrollmentModelQuery = @"Select Id, BusinessMeaningName from ACEnrollmentMode";

            var priceGroupList = new List<BusinessNames>();
            var courseLevelList = new List<BusinessNames>();
            var enrollmentModelList = new List<BusinessNames>();

            try
            {
                priceGroupList = _dataService.Query<BusinessNames>("u4", priceGroupQuery).ToList();
                courseLevelList = _dataService.Query<BusinessNames>("u4", courseLevelQuery).ToList();
                enrollmentModelList = _dataService.Query<BusinessNames>("u4", enrollmentModelQuery).ToList();
            }
            catch (Exception ex)
            {
                throw;
                //We dont need to handle as we just update the record with no ID
            }

            using FileStream rstr = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            IWorkbook workbook = new XSSFWorkbook(rstr);
            var sheet = workbook.GetSheet(sheetName);
            IRow headerRow = sheet.GetRow(1); //Header 
            int cellCount = headerRow.LastCellNum;

            string courseIdColumn = "B";
            string MinEnrolledColumn = "D";
            string MaxEnrolledColumn = "E";
            string PriceGroupIdColumn = "F";
            string CourseLevelIdColumn = "G";
            string EnrollmentModeIdColumn = "H";
            string RecordIdColumn = "I";
            string ResultColumn = "J";
            string ErrorColumn = "K";

            for (int i = (sheet.FirstRowNum + 1); i <= sheet.LastRowNum; i++) //Values from 2nd row
            {
                IRow row = sheet.GetRow(i);
                if (row == null) continue;
                if (row.Cells.All(d => d.CellType == CellType.Blank)) continue;

                string courseId = string.Empty;
                string successValue = string.Empty;
                string invalidDates = string.Empty;
                dynamic errorValue = null;
                int recordId = 0;
                var payload = new CourseOfferingTemplate();
                HttpResponseMessage httpResponseMessage;

                int j = 1; //Leave the first column blank
                //Columns from each row
                for (j = row.FirstCellNum; j < cellCount; j++)
                {
                    if (row.GetCell(j) != null)
                    {
                        var reference = new CellReference(row.GetCell(j)).FormatAsString();
                        ICell cell;

                        //Get the CourseId & fetch RecordID
                        if (reference.StartsWith(courseIdColumn)) //CourseId
                        {
                            courseId = row.GetCell(j).StringCellValue.Trim();
                        }

                        //If we got CourseId then fetch RecordID
                        if (!string.IsNullOrEmpty(courseId))
                        {
                            if (recordId <= 0)
                            {
                                try
                                {
                                    var dynamicParameters = new DynamicParameters();
                                    var query = Assembly.GetExecutingAssembly().GetEmbeddedResource("CourseofferingTemplateQuery.sql");
                                    dynamicParameters.Add("@courseId", courseId);
                                    recordId = _dataService.Query<int>("u4", query, dynamicParameters).FirstOrDefault();
                                    //recordId = 2;
                                    payload.Id = recordId;
                                }
                                catch (Exception ex)
                                {
                                    //We dont need to handle as we just update the record with no ID
                                }
                            }

                            if (recordId > 0)
                            {
                                if (string.IsNullOrEmpty(successValue) && string.IsNullOrEmpty(errorValue))
                                {
                                    if (reference.StartsWith(MinEnrolledColumn)) //MinEnrolled
                                    {
                                        if (string.IsNullOrEmpty(row.GetCell(j).NumericCellValue.ToString()))
                                        {
                                            successValue = "Skip Record";
                                        }
                                        else
                                        {
                                            payload.MinEnrolled = Convert.ToInt32(row.GetCell(j).NumericCellValue);
                                        }
                                    }
                                    if (reference.StartsWith(MaxEnrolledColumn)) //MaxEnrolled
                                    {
                                        if (string.IsNullOrEmpty(row.GetCell(j).NumericCellValue.ToString()))
                                        {
                                            successValue = "Skip Record";
                                        }
                                        else
                                        {
                                            payload.MaxEnrolled = Convert.ToInt32(row.GetCell(j).NumericCellValue);
                                        }
                                    }
                                    if (reference.StartsWith(PriceGroupIdColumn)) //PriceGroupId
                                    {
                                        payload.PriceGroupId = priceGroupList.Find(x => x.BusinessMeaningName == row.GetCell(j).StringCellValue).Id;
                                    }
                                    if (reference.StartsWith(CourseLevelIdColumn)) //CourseLevelId
                                    {
                                        payload.CourseLevelId = courseLevelList.Find(x => x.BusinessMeaningName == row.GetCell(j).StringCellValue).Id;
                                    }
                                    if (reference.StartsWith(EnrollmentModeIdColumn)) //EnrollmentModeId
                                    {
                                        payload.EnrollmentModeId = enrollmentModelList.Find(x => x.BusinessMeaningName == row.GetCell(j).StringCellValue).Id;
                                    }

                                    if (payload.EnrollmentModeId > 0)
                                    {
                                        //Call Api to Update 
                                        try
                                        {
                                            var apiUri = new Uri(_config.BaseUrl + $"/CourseOfferingTemplate/put?id={recordId}");
                                            var payloadString = JsonConvert.SerializeObject(payload);
                                            var message = await _messageFactory.CreateMessage(HttpMethod.Put, apiUri, payloadString);

                                            httpResponseMessage = await _httpClient.SendAsync(message);

                                            if (httpResponseMessage.StatusCode.Equals(System.Net.HttpStatusCode.OK))
                                            {
                                                successValue = "Success";
                                            }
                                            else //failure
                                            {
                                                errorValue = httpResponseMessage.Content.ReadAsStringAsync();
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            errorValue = ex;
                                        }
                                    }
                                }

                                if (reference.StartsWith(RecordIdColumn)) // RecordId
                                {
                                    using FileStream wstr = new FileStream(fileName, FileMode.Create, FileAccess.Write);
                                    cell = row.GetCell(j);
                                    cell.SetCellValue(recordId);
                                    workbook.Write(wstr);
                                    wstr.Close();
                                }

                                if (reference.StartsWith(ResultColumn)) // Result : Success Or Failure
                                {
                                    using FileStream wstr = new FileStream(fileName, FileMode.Create, FileAccess.Write);
                                    cell = row.GetCell(j);
                                    cell.SetCellValue(successValue ?? "Fail");
                                    workbook.Write(wstr);
                                    wstr.Close();
                                }

                                if (reference.StartsWith(ErrorColumn) && errorValue != null) // Error Reason
                                {
                                    using FileStream wstr = new FileStream(fileName, FileMode.Create, FileAccess.Write);
                                    cell = row.GetCell(j);
                                    cell.SetCellValue(errorValue);
                                    workbook.Write(wstr);
                                    wstr.Close();
                                }
                            }
                            else
                            {
                                //Log error to Excel and continue
                                if (reference.StartsWith(ErrorColumn) && (recordId == 0)) // Error Reason
                                {
                                    using FileStream noIDStream = new FileStream(fileName, FileMode.Create, FileAccess.Write);
                                    cell = row.GetCell(j);
                                    cell.SetCellValue("ID does not exist");
                                    workbook.Write(noIDStream);
                                    noIDStream.Close();
                                }
                            }
                        }
                    }
                }
            }
            rstr.Close();
        }

        private bool ValidateDates(string startDate, string endDate)
        {
            return DateTime.TryParse(startDate, out DateTime _) == true &&
                         (DateTime.TryParse(endDate, out DateTime _) == true);

        }
    }
}
