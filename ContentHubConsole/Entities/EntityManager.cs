﻿using Azure;
using ContentHubConsole.Assets;
using ContentHubConsole.ContentHubClients.Covetrus.Assets.SmartPak;
using Stylelabs.M.Base.Querying;
using Stylelabs.M.Base.Querying.Linq;
using Stylelabs.M.Framework.Essentials.LoadOptions;
using Stylelabs.M.Sdk.Contracts.Base;
using Stylelabs.M.Sdk.WebClient;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ContentHubConsole.Entities
{
    public class EntityManager
    {
        private readonly IWebMClient _webMClient;

        public EntityManager(IWebMClient webMClient)
        {
            _webMClient = webMClient;
        }

        public async Task<long> UpdateEntityProperties(long entityId, List<KeyValuePair<string, string>> properties)
        {
            try
            {
                var entity = await _webMClient.Entities.GetAsync(entityId);
                await entity.LoadPropertiesAsync(PropertyLoadOption.All);

                foreach (var kv in properties)
                {
                    entity.SetPropertyValue(kv.Key, kv.Value);
                }

                return await _webMClient.Entities.SaveAsync(entity);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("UpdateEntityProperties", ex.Message);

                return 0;
            }
        }

        public async Task<long> AddEntityRelation(long entityId, string relationName, long relationId)
        {
            try
            {
                var entity = await _webMClient.Entities.GetAsync(entityId);
                await entity.LoadRelationsAsync(RelationLoadOption.All);

                var relation = entity.GetRelation(relationName);
                var currentIds = relation.GetIds();
                currentIds.Add(relationId);
                relation.SetIds(currentIds);

                return await _webMClient.Entities.SaveAsync(entity);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                FileLogger.Log("AddEntityRelation", ex.Message);
                return 0;
            }
        }

        public async Task<List<FileUploadResponse>> GetMigratedAssetsWithNoType(bool checkOriginPath)
        {
            var fileUploadResponses = new List<FileUploadResponse>();
            var dateMin = new DateTime(2022, 12, 16);
            int skip = 0;
            var take = 3000;

            try
            {
                Query query = Query.CreateQuery(entities =>
                 (from e in entities
                  where e.DefinitionName == "M.Asset"
                    && e.Parent("AssetTypeToAsset") == null
                    //&& e.ModifiedByUsername == "patrick.jones@xcentium.com"
                    && e.ModifiedOn >= dateMin
                  select e).Skip(skip).Take(take));

                if (checkOriginPath)
                {
                    query = Query.CreateQuery(entities =>
                     (from e in entities
                      where e.DefinitionName == "M.Asset"
                        && e.Parent("AssetTypeToAsset") == null
                        //&& e.ModifiedByUsername == "patrick.jones@xcentium.com"
                        && e.Property("OriginPath").Contains("CONA Creative Campaigns")
                        && e.Property("OriginPath").Contains("2021")
                        && e.Property("OriginPath").Contains("Strategic Accounts")
                        //&& e.Property("OriginPath").Contains("Photography")
                        //&& (e.Property("Title") == "3006157_3004723_ENG_LEFT.jpg")
                        && e.ModifiedOn > dateMin
                      select e).Skip(skip).Take(take));
                }

                var mq = await _webMClient.Querying.QueryAsync(query);


                if (mq.Items.Any())
                {
                    Console.WriteLine($"Assets found: {mq.Items.Count}");
                    var tags = mq.Items.ToList();
                    var assets = tags.Select(s => new { AsssetId = s.Id.Value, Filename = s.GetPropertyValue<string>("FileName") } ).ToList();
                    //return (assets.AsssetId, assets.Filename);

                    
                    foreach (var asset in assets)
                    {
                        try
                        {
                            var files = Directory.GetFiles(PhotographyBasicAssetDetailer.UploadPath, asset.Filename, SearchOption.AllDirectories);
                            if (files.Any() && files.Count() == 1)
                            {
                                fileUploadResponses.Add(new FileUploadResponse(asset.AsssetId, files.FirstOrDefault()));
                            }

                            if (files.Any() && files.Count() > 1)
                            {
                                var filtered = files.Except(fileUploadResponses.Select(s => s.LocalPath).ToList());
                                fileUploadResponses.Add(new FileUploadResponse(asset.AsssetId, filtered.FirstOrDefault()));
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error getting path for asset id: {asset.AsssetId}. Messsage: {ex.Message}");
                            continue;
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No assets found.");
                }

                //if (!String.IsNullOrEmpty(tagValueTrimmed))
                //{
                //    var tagEntity = await _webMClient.EntityFactory.CreateAsync(M_TAG);
                //    tagEntity.Identifier = $"{M_TAG}.{tagValueTrimmed}";
                //    tagEntity.SetPropertyValue("TagName", tagLower);
                //    tagEntity.SetPropertyValue("TagLabel", CultureInfo.CurrentCulture, tagLower);

                //    return await _webMClient.Entities.SaveAsync(tagEntity);
                //}
            }
            catch (Exception ex)
            {
                var error = $"Error assets";
                Console.WriteLine(error);
                FileLogger.Log("AddTagValue", error);
            }
            Console.WriteLine($"Assets getting updated: {fileUploadResponses.Count}");
            return fileUploadResponses;
        }

        public async Task<List<FileUploadResponse>> GetMigratedAssetsWithPathAndNoType(bool checkOriginPath)
        {
            var iEntities = new List<IEntity>();
            var fileUploadResponses = new List<FileUploadResponse>();
            var dateMin = new DateTime(2022, 12, 29);
            int curSkip = 0;
            int curTake = 2000;
            bool canQuery = true;

            try
            {
                while (canQuery)
                {
                    Query query = Query.CreateQuery(entities =>
                     (from e in entities
                      where e.DefinitionName == "M.Asset"
                        && e.Parent("AssetTypeToAsset") == null
                        //&& e.ModifiedByUsername == "patrick.jones@xcentium.com"
                        && e.ModifiedOn >= dateMin
                      select e).Skip(curSkip).Take(curTake));

                    if (checkOriginPath)
                    {
                        query = Query.CreateQuery(entities =>
                         (from e in entities
                          where e.DefinitionName == "M.Asset"
                            && e.Parent("AssetTypeToAsset") == null
                            && e.Parent("BusinessDomainToAsset").In(32585)
                            //&& e.ModifiedByUsername == "patrick.jones@xcentium.com"
                            && e.Property("OriginPath").Contains("Dropbox (Covetrus)")
                            && e.Property("OriginPath").Contains(" Creative Campaigns (1)")
                            && (e.Property("OriginPath").Contains("2020") || e.Property("OriginPath").Contains("2021"))
                            && e.Property("OriginPath").Contains("Equipment")
                            //&& e.Property("OriginPath").Contains("Photography")
                            //&& (e.Property("Title") == "3006157_3004723_ENG_LEFT.jpg")
                            && e.ModifiedOn > dateMin
                          select e).Skip(curSkip).Take(curTake));
                    }

                    if (Program.TestMode)
                    {
                        var mq = await _webMClient.Querying.QueryAsync(query);
                        iEntities.AddRange(mq.Items.Take(Program.TestModeTake).ToList());
                        canQuery = false;
                    }
                    else
                    {
                        var mq = await _webMClient.Querying.QueryAsync(query);
                        if (mq.TotalNumberOfResults > 0 && iEntities.Count <= 7000)
                        {
                            iEntities.AddRange(mq.Items.ToList());
                            curSkip = curSkip + curTake;
                            if (mq.Items.Count < curTake)
                            {
                                canQuery = false;
                            }
                        }
                        else
                        {
                            canQuery = false;
                        }
                    }
                }

                if (iEntities.Any())
                {
                    Console.WriteLine($"Assets found: {iEntities.Count}");
                    var tags = iEntities.ToList();
                    var assets = tags.Select(s => new { AsssetId = s.Id.Value, OriginPath = s.GetPropertyValue<string>("OriginPath") }).ToList();


                    foreach (var asset in assets)
                    {
                        try
                        {
                            fileUploadResponses.Add(new FileUploadResponse(asset.AsssetId, asset.OriginPath));
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error getting path for asset id: {asset.AsssetId}. Messsage: {ex.Message}");
                            continue;
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No assets found.");
                }

            }
            catch (Exception ex)
            {
                var error = $"Error assets";
                Console.WriteLine(error);
                FileLogger.Log("AddTagValue", error);
            }
            Console.WriteLine($"Assets getting updated: {fileUploadResponses.Count}");
            return fileUploadResponses;
        }


        public async Task RemoveAssets(bool delete = false)
        {
            try
            {
                var query = Query.CreateQuery(entities =>
                 (from e in entities
                  where e.DefinitionName == "M.Asset" && e.Property("OriginPath").Contains("SmartPak") && e.Property("OriginPath").Contains("Logo_Art")
                  select e).Skip(0).Take(3000));

                var mq = await _webMClient.Querying.QueryAsync(query);

                if (mq.Items.Any())
                {
                    Console.WriteLine($"Found: {mq.Items.Count}");
                    foreach (var item in mq.Items.ToList())
                    {
                        var path = item.GetPropertyValue<string>("OriginPath");
                        if (delete)
                        {
                            Console.WriteLine($"Deleting: {path}");
                            await _webMClient.Entities.DeleteAsync(item.Id.Value);
                        }
                        else
                        {
                            Console.WriteLine($"C:\\Users\\ptjhi{path}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
            }
        }
    }
}
