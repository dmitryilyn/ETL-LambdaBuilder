﻿using org.ohdsi.cdm.framework.common.Base;
using org.ohdsi.cdm.framework.common.Builder;
using org.ohdsi.cdm.framework.common.Extensions;
using org.ohdsi.cdm.framework.common.Lookups;
using org.ohdsi.cdm.framework.common.Omop;
using System;
using System.Collections.Generic;
using System.Linq;

namespace org.ohdsi.cdm.framework.common.Core.Transformation.CDM
{
    /// <summary>
    ///  Implementation of PersonBuilder for CPRD, based on CDM Build spec
    /// </summary>
    public class CdmPersonBuilder : PersonBuilder
    {
        private readonly Dictionary<long, bool> _removedVisitIds = [];
        private readonly Dictionary<long, bool> _removedVisitDetailIds = [];

        public override KeyValuePair<Person, Attrition> BuildPerson(List<Person> records)
        {
            if (records == null || records.Count == 0)
                return new KeyValuePair<Person, Attrition>(null, Attrition.UnacceptablePatientQuality);

            var ordered = records.OrderByDescending(p => p.StartDate).ToArray();
            var person = ordered.Take(1).First();
            person.StartDate = ordered.Take(1).Last().StartDate;

            if (person.YearOfBirth < 1875)
                return new KeyValuePair<Person, Attrition>(null, Attrition.ImplausibleYOBPast);

            if (person.YearOfBirth > DateTime.Now.Year)
                return new KeyValuePair<Person, Attrition>(null, Attrition.ImplausibleYOBFuture);

            return new KeyValuePair<Person, Attrition>(person, Attrition.None);
        }

        private void TryToRemap(IEnumerable<IEntity> records)
        {
            foreach (var record in records)
            {
                if(record.VisitOccurrenceId.HasValue && _removedVisitIds.ContainsKey(record.VisitOccurrenceId.Value))
                {
                    record.VisitOccurrenceId = null;
                }

                if (record.VisitDetailId.HasValue && _removedVisitDetailIds.ContainsKey(record.VisitDetailId.Value))
                {
                    record.VisitDetailId = null;
                }

               UpdateLookup(record);
            }
        }

        private void UpdateLookup(IEntity e)
        {
            var sourceVocabularyId = Vocabulary.GetSourceVocabularyId(e.SourceConceptId);
            string lookupName = null;
            if (!string.IsNullOrEmpty(sourceVocabularyId))
            {
                switch (sourceVocabularyId.ToLower())
                {
                    case "cpt4":
                        lookupName = "cpt4";
                        break;
                    case "hcpcs":
                        lookupName = "hcpcs";
                        break;
                    case "icd10cm":
                        lookupName = "icd10cm";
                        break;
                    case "icd10pcs":
                        lookupName = "icd10pcs";
                        break;
                    case "icd9cm":
                        lookupName = "icd9cm";
                        break;
                    case "icd9proc":
                        lookupName = "icd9proc";
                        break;
                    case "ndc":
                        lookupName = "ndc";
                        break;
                    case "revenue code":
                        lookupName = "revenue_code";
                        break;
                }
            }
            if (!string.IsNullOrEmpty(lookupName)) 
            {
                var lookup = Vocabulary.Lookup(e.SourceValue, lookupName, DateTime.MinValue);

                foreach (var l in lookup)
                {
                    if (l.SourceValidStartDate.Year <= 1900)
                        continue;

                    if (l.SourceConceptId > 0 && e.StartDate.Between(l.SourceValidStartDate, l.SourceValidEndDate))
                    {
                        e.SourceConceptId = l.SourceConceptId;
                    }

                    if (l.ConceptId.HasValue && l.ConceptId.Value > 0 && e.StartDate.Between(l.ValidStartDate, l.ValidEndDate))
                    {
                        e.ConceptId = l.ConceptId.Value;
                    }
                }
            }
        }

        public override Attrition Build(ChunkData data, KeyMasterOffsetManager o)
        {
            this.Offset = o;
            this.ChunkData = data;

            var result = BuildPerson([.. PersonRecords]);
            var person = result.Key;
            if (person == null)
            {
                Complete = true;
                return result.Value;
            }

            if(ObservationPeriodsRaw.Count == 0)
                return Attrition.InvalidObservationTime;

            var observationPeriods = new List<ObservationPeriod>();
            foreach (var op in ObservationPeriodsRaw)
            {
                if(op.StartDate.Date > DateTime.Now.Date)
                    return Attrition.InvalidObservationTime;

                if (op.EndDate.Value.Date > DateTime.Now.Date)
                    op.EndDate = DateTime.Now.Date;

                if (op.StartDate.Date > op.EndDate.Value.Date)
                    return Attrition.InvalidObservationTime;

                observationPeriods.Add(new ObservationPeriod
                {
                    Id = op.Id,
                    PersonId = op.PersonId,
                    StartDate = op.StartDate,
                    EndDate = op.EndDate,
                    TypeConceptId = op.TypeConceptId,
                });
            }

            var payerPlanPeriods = PayerPlanPeriodsRaw.ToArray();
            var visitOccurrences = new Dictionary<long, VisitOccurrence>();

            foreach (var visitOccurrence in VisitOccurrencesRaw)
            {
                if(visitOccurrence.StartDate.Date > DateTime.Now.Date)
                {
                    _removedVisitIds.TryAdd(visitOccurrence.Id, false);
                    continue;
                }

                visitOccurrences.Add(visitOccurrence.Id, visitOccurrence);
            }

            var visitDetails = new List<VisitDetail>();

            foreach (var vd in VisitDetailsRaw)
            {
                if (vd.StartDate.Date > DateTime.Now.Date)
                {
                    _removedVisitDetailIds.TryAdd(vd.Id, false);
                    continue;
                }

                visitDetails.Add(vd);
            }

            TryToRemap(DrugExposuresRaw);
            TryToRemap(ConditionOccurrencesRaw);
            TryToRemap(ProcedureOccurrencesRaw);
            TryToRemap(ObservationsRaw);
            TryToRemap(MeasurementsRaw);
            TryToRemap(DeviceExposureRaw);

            var drugExposures = DrugExposuresRaw.ToArray();
            var conditionOccurrences = ConditionOccurrencesRaw.ToArray();
            var procedureOccurrences = ProcedureOccurrencesRaw.ToArray();
            var observations = ObservationsRaw.ToArray();
            var measurements = MeasurementsRaw.ToArray();
            var deviceExposure = DeviceExposureRaw.ToArray();


            Death death = DeathRecords.FirstOrDefault();

            if(death != null && death.StartDate.Date > DateTime.Now.Date)
                death = null;

            // push built entities to ChunkBuilder for further save to CDM database
            AddToChunk(person, death, [.. observationPeriods], payerPlanPeriods, drugExposures,
                conditionOccurrences, procedureOccurrences, observations, measurements,
                [.. visitOccurrences.Values], [.. visitDetails], null, deviceExposure, null, null);

            Complete = true;

            var pg = new PregnancyAlgorithm.PregnancyAlgorithm();
            foreach (var r in pg.GetPregnancyEpisodes(Vocabulary, person, observationPeriods.ToArray(),
                ChunkData.ConditionOccurrences.Where(e => e.PersonId == person.PersonId).ToArray(),
                ChunkData.ProcedureOccurrences.Where(e => e.PersonId == person.PersonId).ToArray(),
                ChunkData.Observations.Where(e => e.PersonId == person.PersonId).ToArray(),
                ChunkData.Measurements.Where(e => e.PersonId == person.PersonId).ToArray(),
                ChunkData.DrugExposures.Where(e => e.PersonId == person.PersonId).ToArray()))
            {
                r.Id = Offset.GetKeyOffset(r.PersonId).ConditionEraId;
                ChunkData.ConditionEra.Add(r);
            }

            return Attrition.None;
        }

    }
}