﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WrapRec
{
    public class CrossDomainDataContainer : DataContainer
    {
        public Dictionary<string, Domain> Domains { get; private set; }

        private Domain _activeDomain;

        private static Domain _defaultDomain;

        /// <summary>
        /// This is to specify the domain to which the data is being loaded
        /// </summary>
        public Domain ActiveDomain 
        {
            get
            {
                return _activeDomain;
            }
            set
            {
                if (!Domains.ContainsKey(value.Id))
                {
                    AddDomain(value);
                    _activeDomain = value;
                }
                else
                {
                    _activeDomain = Domains[value.Id];
                }
            }
        }

        public CrossDomainDataContainer()
            : base()
        {
            Domains = new Dictionary<string, Domain>();
        }

        /// <summary>
        /// This method return the default domain for single domain scenarios (this is necessary because the ratings for each user is added to a domain)
        /// (should be tested)
        /// </summary>
        /// <returns></returns>
        public static Domain GetDefualtDomain()
        {
            if (_defaultDomain == null)
                _defaultDomain = new Domain("default");

            return _defaultDomain;
        }
        
        public void AddDomain(Domain domain)
        {
            Domains.Add(domain.Id, domain);
        }

        public override ItemRating AddRating(string userId, string itemId, float rating, bool isTest)
        {
            if (ActiveDomain == null)
            {
                throw new Exception(string.Format("Active dmain is not defined in the CrossDomainDataContainer"));
            }

            // ItemId is added with domainId to make sure that items in different domains have different ids
            var ir = base.AddRating(userId, itemId + ActiveDomain.Id, rating, isTest);

            ir.Domain = ActiveDomain;
            ActiveDomain.Ratings.Add(ir);

            return ir;
        }

        public override string ToString()
        {
            return string.Format("{0} Domains, {1} Users, {2} Items, {3} Ratings", Domains.Count, Users.Count, Items.Count, Ratings.Count);
        }

        public void PrintStatistics()
        {
            Console.WriteLine("Data Container Statistics: \n{0} \nDomains:", ToString());
            Domains.ToList().ForEach(d => Console.WriteLine(d.ToString()));
            Console.WriteLine("Data statistics: \nNum Test Samples: {0}", Ratings.Where(r => r.IsTest == true).Count());
        }

        public void WriteHistogram(string outputFolder)
        {
            foreach (var domain in Domains.Values)
            {
                domain.WriteHistogram(string.Format("{0}\\{1}.usershist.csv", outputFolder, domain.Id));
            }
        }
    }
}