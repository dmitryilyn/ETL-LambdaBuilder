﻿{base},
Standard as (
SELECT distinct SOURCE_CODE, TARGET_CONCEPT_ID, TARGET_DOMAIN_ID, SOURCE_VALID_START_DATE as VALID_START_DATE, SOURCE_VALID_END_DATE as VALID_END_DATE, SOURCE_VOCABULARY_ID
FROM Source_to_Standard
WHERE lower(SOURCE_VOCABULARY_ID) = 'jnj_optum_ehr_oncmet'
AND (TARGET_INVALID_REASON IS NULL or TARGET_INVALID_REASON = '')
)

select distinct Standard.*
from Standard