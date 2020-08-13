using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EdmxToFluentApi
{
    class Program
    {
        static void Main(string[] args)
        {
            Directory.CreateDirectory("d:\\tmp\\freteiro\\Config");
            XDocument doc = XDocument.Load(args[0]);

            XElement root = doc.Root;

            var dic = new Dictionary<string, (string, StringBuilder)>();
            var EntityContainer = root.Descendants().Where(x => x.Name.LocalName == "EntityContainer").FirstOrDefault();
            var ConceptualModels = root.Descendants().Where(x => x.Name.LocalName == "ConceptualModels").Descendants().Where(x => x.Name.LocalName == "EntityType").ToList();

            XNamespace ns = EntityContainer.GetDefaultNamespace();

            foreach (var entity in EntityContainer.Elements(ns + "EntitySet"))
            {
                try
                {

                    var entityName = entity.Attributes("Name").FirstOrDefault().Value;
                    var schema = entity.Attributes("Schema").FirstOrDefault()?.Value;
                    dic.Add(entityName, (schema, new StringBuilder()));

                }
                catch (Exception)
                {

                    //  throw;
                }
            }

            var entities = root.Descendants().Where(x => x.Name.LocalName == "EntityType").ToList();

            foreach (var item in dic)
            {
                var entity = entities.Where(p => p.Attributes("Name").FirstOrDefault().Value == item.Key).FirstOrDefault();
                {
                    var entityName = entity.Attributes("Name").FirstOrDefault().Value;

                    ns = entity.GetDefaultNamespace();

                    var keys = new List<string>();
                    foreach (var element in entity.Elements(ns + "Key").FirstOrDefault().Elements())
                    {
                        var field = element.Attributes("Name").FirstOrDefault().Value;

                        keys.Add(field);
                    }

                    RenderKey(entityName, keys, item.Value.Item2);


                    foreach (var element in entity.Elements(ns + "Property"))
                    {
                        var field = element.Attributes("Name").FirstOrDefault().Value;
                        var Type = element.Attributes("Type").FirstOrDefault().Value;
                        var Nullable = element.Attributes("Nullable").FirstOrDefault()?.Value;
                        var MaxLength = element.Attributes("MaxLength").FirstOrDefault()?.Value;
                        var Precision = element.Attributes("Precision").FirstOrDefault()?.Value;
                        var Scale = element.Attributes("Scale").FirstOrDefault()?.Value;

                        RenderField(entityName, field, Type, Nullable, MaxLength, Precision, Scale, item.Value.Item2);
                    }
                }

            }

            var associations = root.Descendants().Where(x => x.Name.LocalName == "Association").ToList();

            foreach (var association in associations)
            {
                ns = association.GetDefaultNamespace();
                var name = association.Attributes("Name").FirstOrDefault().Value;
                try
                {
                    var ReferentialConstraint = association.Elements(ns + "ReferentialConstraint").FirstOrDefault();
                    var principal = ReferentialConstraint.Elements(ns + "Principal").FirstOrDefault().Attributes("Role").FirstOrDefault().Value;
                    var principalFields = ReferentialConstraint.Elements(ns + "Principal").FirstOrDefault().Elements().Select(p => p.Attributes("Name").FirstOrDefault().Value).ToList();
                    var dependent = ReferentialConstraint.Elements(ns + "Dependent").FirstOrDefault().Attributes("Role").FirstOrDefault().Value;
                    var dependentFields = ReferentialConstraint.Elements(ns + "Dependent").FirstOrDefault().Elements().Select(p => p.Attributes("Name").FirstOrDefault().Value).ToList();

                    var principalMultiplicity = association.Elements(ns + "End").Where(p => p.Attributes("Role").FirstOrDefault().Value == principal).FirstOrDefault().Attributes("Multiplicity").FirstOrDefault().Value;
                    var dependentMultiplicity = association.Elements(ns + "End").Where(p => p.Attributes("Role").FirstOrDefault().Value == dependent).FirstOrDefault().Attributes("Multiplicity").FirstOrDefault().Value;


                    var item = ConceptualModels.Where(p => p.Attributes("Name").FirstOrDefault().Value == principal).FirstOrDefault();
                    var ns1 = item.GetDefaultNamespace();
                    var principalName = item.Elements(ns1 + "NavigationProperty").Where(p =>
                    p.Attributes("Relationship").FirstOrDefault().Value.Contains(name) &&
                    p.Attributes("FromRole").FirstOrDefault().Value == principal &&
                    p.Attributes("ToRole").FirstOrDefault().Value == dependent
                    ).FirstOrDefault().Attributes("Name").FirstOrDefault().Value;

                    item = ConceptualModels.Where(p => p.Attributes("Name").FirstOrDefault().Value == dependent).FirstOrDefault();
                    ns1 = item.GetDefaultNamespace();
                    var dependentName = item.Elements(ns1 + "NavigationProperty").Where(p =>
                    p.Attributes("Relationship").FirstOrDefault().Value.Contains(name) &&
                    p.Attributes("FromRole").FirstOrDefault().Value == dependent &&
                    p.Attributes("ToRole").FirstOrDefault().Value == principal
                    ).FirstOrDefault().Attributes("Name").FirstOrDefault().Value;

                    RenderAssociation(name, principal, principalName, principalFields, principalMultiplicity, dependent, dependentName, dependentFields, dependentMultiplicity, dic[dependent].Item2);
                }
                catch
                {
                    Console.WriteLine(name);
                }
            }



            foreach (var item in dic)
            {
                WriteFileConfig(item);
            }

        }

        private static void WriteFileConfig(KeyValuePair<string, (string, StringBuilder)> item)
        {
            if (item.Value.Item1 != "freteiro")
                return;

            var fileString = @"using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using portalfreteiro.Models;

namespace Freteiro.Infra.Data.Config
{
    public class " + item.Key + @"Configuration : IEntityTypeConfiguration<" + item.Key + @">
    {
        public void Configure(EntityTypeBuilder<" + item.Key + @"> builder)
        {
"
        +
        item.Value.Item2.ToString()
        +
@"      }
    }
}
";

            try
            {
                File.WriteAllText(@"d:\tmp\freteiro\Config\" + item.Key + "Configuration.cs", fileString);

            }
            catch (Exception)
            {
            }
        }

        private static void RenderAssociation(string fkName, string principal, string principalName, List<string> principalFields, string principalMultiplicity, string dependent, string dependentName, List<string> dependentFields, string dependentMultiplicity, StringBuilder item2)
        {
            item2.AppendLine($"            builder.HasOne(p => p.{dependentName}).WithMany(p => p.{principalName}).HasForeignKey(p => p.{dependentFields[0]}).HasConstraintName(\"{fkName}\");");
        }

        private static void RenderField(string entityName, string field, string type, string nullable, string maxLength, string precision, string scale, StringBuilder item2)
        {
            var isRequired = string.Empty;
            var maxLengthStr = string.Empty;
            var decimalType = string.Empty;
            if (nullable == "false")
                isRequired = ".IsRequired()";
            if (!string.IsNullOrEmpty(maxLength))
                maxLengthStr = $".HasMaxLength({maxLength})";

            if (!string.IsNullOrEmpty(precision))
                decimalType = $".HasColumnType(\"{type}({precision}, {scale})\")";

            if (!string.IsNullOrEmpty(isRequired) || !string.IsNullOrEmpty(maxLength) || !string.IsNullOrEmpty(decimalType))
                item2.AppendLine($"            builder.Property(p => p.{field}){isRequired}{maxLengthStr}{decimalType};");

            //builder.Entity<FretesXGerenciadorasRisco>().Property(p => p.Country).IsRequired().HasMaxLength(30);
        }

        private static void RenderKey(string entityName, List<string> keys, StringBuilder item2)
        {
            var keyStr = string.Empty;
            if (keys.Count() == 1)
                keyStr = " p." + keys[0];
            else
                keyStr = "new { p." + string.Join(", p.", keys) + " }";

            item2.AppendLine($"            builder.HasKey(p =>{keyStr});");
        }
    }
}
