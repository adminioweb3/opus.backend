http://localhost:8088/api/onboarding/analyze

paylaod : {
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"websiteUrl": "https://ioweb3.io/",
"businessName": "Ioweb3 Technology",
"industry": "B2B , SaaS",
"targetAudience": "Enterprise IT managers",
"keywords": ""
}

response : {
"businessSummary": {
"value": "Ioweb3 Technology is a B2B SaaS provider specializing in software engineering, offering product development, cloud and DevOps solutions, and quality assurance services. The company focuses on building and scaling digital products for startups and enterprises, leveraging modern technology stacks.",
"confidence": 90
},
"coreServices": {
"value": [
"Generative AI Development",
"SaaS App Development",
"Product Engineering",
"Cloud & DevOps Solutions",
"Remote Full Stack Team",
"Quality Assurance Services"
],
"confidence": 90
},
"products": {
"value": [],
"confidence": 0
},
"industriesServed": {
"value": [
"Fintech",
"Healthcare",
"E-commerce",
"Education",
"Logistics",
"Entertainment"
],
"confidence": 80
},
"businessModel": {
"value": "B2B",
"confidence": 100
},
"uniqueSellingProposition": {
"value": "Ioweb3 differentiates itself through a strong emphasis on collaboration, scaling digital products with cutting-edge technologies, and providing dedicated engineering teams tailored to client needs.",
"confidence": 80
},
"primaryTechnologies": {
"value": [
"Angular",
"React",
"Vue.js",
"Next.js",
"Node.js",
"Firebase",
"PWA",
"Flutter",
"Electron",
"Ionic"
],
"confidence": 90
},
"targetCustomers": {
"value": [
"Enterprise IT Managers",
"Startups",
"Enterprises"
],
"confidence": 80
},
"contentCategories": {
"value": [
"Blogs",
"Case Studies",
"Testimonials",
"Careers"
],
"confidence": 70
},
"seoStrength": {
"value": {
"overall": "The website has a good SEO foundation with clear service descriptions and some keyword optimization for target services.",
"score": 75,
"strengths": [
"Clear metadata and headings",
"Service-oriented content",
"User-friendly navigation"
],
"weaknesses": [
"Limited structured data usage",
"Potential improvement in internal linking"
],
"recommendations": [
"Enhance structured data implementation",
"Increase content depth on specific services"
]
},
"confidence": 80
},
"websiteStructure": {
"value": {
"navigationQuality": "Good, with clear access to services and contact information.",
"importantPages": [
"Homepage",
"About",
"Services",
"Contact",
"Blog"
],
"blogPresent": true,
"contactPresent": true,
"pricingPresent": false,
"faqPresent": false,
"mobileFriendlyEstimate": "Mobile-friendly",
"overallArchitecture": "Well-structured with a clear service listing."
},
"confidence": 90
},
"domainAuthorityEstimate": {
"value": {
"estimatedScore": 50,
"category": "Medium",
"reason": "The website has quality content and services but may lack significant backlinks and brand recognition."
},
"confidence": 70
},
"topicalAuthority": {
"value": {
"primaryTopics": [
"SaaS Development",
"AI Engineering",
"Cloud Solutions"
],
"authorityLevel": "Medium",
"reason": "Ioweb3 covers relevant topics but could enhance its authority with more specialized content and case studies."
},
"confidence": 70
},
"brandPositioning": {
"value": "Ioweb3 positions itself as a reliable technological partner for enterprises seeking robust SaaS solutions and digital product engineering.",
"confidence": 80
},
"toneOfVoice": {
"value": {
"primaryTone": "Professional",
"secondaryTone": [
"Technical",
"Friendly"
],
"writingStyle": "Informative",
"readingLevel": "Intermediate"
},
"confidence": 80
},
"overallConfidence": 78
}

http://localhost:8088/api/onboarding/analyze-competitors

payload : {
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd"
}

response : {
"success": true,
"error": null,
"totalCompetitors": 6,
"competitors": [
{
"id": "27af4c06-867d-4e5f-9561-35f044250071",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"name": "ConsenSys",
"websiteUrl": "https://consensys.net",
"industry": "Fintech",
"description": "Blockchain software technology company.",
"category": "Direct",
"logo": null,
"country": null,
"authority": 0,
"popularity": 0,
"rank": 1,
"similarityScore": 100,
"rawJson": "{\"rank\": 1, \"website\": \"https://consensys.net\", \"industry\": \"Fintech\", \"confidence\": 90, \"companyName\": \"ConsenSys\", \"description\": \"Blockchain software technology company.\", \"competitorType\": \"Direct\", \"similarityScore\": 100}",
"createdAt": "2026-07-02T04:53:10.447678Z",
"enrichmentStatus": "Pending",
"enrichedJson": null,
"enrichedAt": null,
"competitorType": "Direct",
"confidence": 90
},
{
"id": "1459b7d0-775c-4eea-88a9-85d8cc57dc96",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"name": "LeewayHertz",
"websiteUrl": "https://www.leewayhertz.com",
"industry": "Fintech",
"description": "Blockchain development services for enterprises.",
"category": "Direct",
"logo": null,
"country": null,
"authority": 0,
"popularity": 0,
"rank": 2,
"similarityScore": 100,
"rawJson": "{\"rank\": 2, \"website\": \"https://www.leewayhertz.com\", \"industry\": \"Fintech\", \"confidence\": 88, \"companyName\": \"LeewayHertz\", \"description\": \"Blockchain development services for enterprises.\", \"competitorType\": \"Direct\", \"similarityScore\": 100}",
"createdAt": "2026-07-02T04:53:10.44984Z",
"enrichmentStatus": "Pending",
"enrichedJson": null,
"enrichedAt": null,
"competitorType": "Direct",
"confidence": 88
},
{
"id": "5b49bfec-1a16-4bbf-8aba-cbf4056a4f94",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"name": "Vention",
"websiteUrl": "https://vention.io",
"industry": "E-commerce",
"description": "Platform for automating industrial operations.",
"category": "Direct",
"logo": null,
"country": null,
"authority": 0,
"popularity": 0,
"rank": 3,
"similarityScore": 100,
"rawJson": "{\"rank\": 3, \"website\": \"https://vention.io\", \"industry\": \"E-commerce\", \"confidence\": 85, \"companyName\": \"Vention\", \"description\": \"Platform for automating industrial operations.\", \"competitorType\": \"Direct\", \"similarityScore\": 100}",
"createdAt": "2026-07-02T04:53:10.449872Z",
"enrichmentStatus": "Pending",
"enrichedJson": null,
"enrichedAt": null,
"competitorType": "Direct",
"confidence": 85
},
{
"id": "f25a3090-1ae2-4cd0-a558-618242fa230c",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"name": "Finastra",
"websiteUrl": "https://www.finastra.com",
"industry": "Fintech",
"description": "Provides financial software solutions for enterprises.",
"category": "Direct",
"logo": null,
"country": null,
"authority": 0,
"popularity": 0,
"rank": 4,
"similarityScore": 98,
"rawJson": "{\"rank\": 4, \"website\": \"https://www.finastra.com\", \"industry\": \"Fintech\", \"confidence\": 78, \"companyName\": \"Finastra\", \"description\": \"Provides financial software solutions for enterprises.\", \"competitorType\": \"Direct\", \"similarityScore\": 98}",
"createdAt": "2026-07-02T04:53:10.449876Z",
"enrichmentStatus": "Pending",
"enrichedJson": null,
"enrichedAt": null,
"competitorType": "Direct",
"confidence": 78
},
{
"id": "32e730a1-b6a2-4a32-a24f-0dbf00929131",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"name": "Salesforce",
"websiteUrl": "https://www.salesforce.com",
"industry": "E-commerce",
"description": "Cloud-based software for customer relationship management.",
"category": "Indirect",
"logo": null,
"country": null,
"authority": 0,
"popularity": 0,
"rank": 5,
"similarityScore": 95,
"rawJson": "{\"rank\": 5, \"website\": \"https://www.salesforce.com\", \"industry\": \"E-commerce\", \"confidence\": 85, \"companyName\": \"Salesforce\", \"description\": \"Cloud-based software for customer relationship management.\", \"competitorType\": \"Indirect\", \"similarityScore\": 95}",
"createdAt": "2026-07-02T04:53:10.449885Z",
"enrichmentStatus": "Pending",
"enrichedJson": null,
"enrichedAt": null,
"competitorType": "Indirect",
"confidence": 85
},
{
"id": "c6c8f0b8-b5b5-4d08-874a-a52f8a8abf3a",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"name": "IBM",
"websiteUrl": "https://www.ibm.com",
"industry": "Fintech",
"description": "Global technology company specializing in cloud and AI.",
"category": "Indirect",
"logo": null,
"country": null,
"authority": 0,
"popularity": 0,
"rank": 6,
"similarityScore": 93,
"rawJson": "{\"rank\": 6, \"website\": \"https://www.ibm.com\", \"industry\": \"Fintech\", \"confidence\": 80, \"companyName\": \"IBM\", \"description\": \"Global technology company specializing in cloud and AI.\", \"competitorType\": \"Indirect\", \"similarityScore\": 93}",
"createdAt": "2026-07-02T04:53:10.44989Z",
"enrichmentStatus": "Pending",
"enrichedJson": null,
"enrichedAt": null,
"competitorType": "Indirect",
"confidence": 80
}
],
"enrichmentQueued": true
}

http://localhost:8088/api/onboarding/analyze-prompts

payload : {
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd"
}

response :{
"success": true,
"error": null,
"totalPrompts": 222,
"prompts": [
{
"id": "e04faa81-c96f-4c69-8325-432c71854385",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "What are the benefits of generative AI development for startups?",
"searchEngine": "Google",
"topic": "Generative AI Development",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"Generative AI Development\", \"prompt\": \"What are the benefits of generative AI development for startups?\", \"promptId\": \"PROMPT-001\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.18893Z"
},
{
"id": "78afcf05-7d1e-4097-b377-eb7679be8e33",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "How can SaaS app development improve my business operations?",
"searchEngine": "Google",
"topic": "SaaS App Development",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"SaaS App Development\", \"prompt\": \"How can SaaS app development improve my business operations?\", \"promptId\": \"PROMPT-002\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189192Z"
},
{
"id": "b5ea64f4-48b3-4634-8fff-f665a5bdda69",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "What features should I consider in a product engineering service?",
"searchEngine": "Google",
"topic": "Product Engineering",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"Product Engineering\", \"prompt\": \"What features should I consider in a product engineering service?\", \"promptId\": \"PROMPT-003\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189204Z"
},
{
"id": "d926d15e-b870-4f6a-919c-8353d0798cdd",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "Can cloud solutions help streamline my IT infrastructure?",
"searchEngine": "Google",
"topic": "Cloud & DevOps Solutions",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"Cloud & DevOps Solutions\", \"prompt\": \"Can cloud solutions help streamline my IT infrastructure?\", \"promptId\": \"PROMPT-004\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189216Z"
},
{
"id": "08f45df8-dff8-4161-a913-fcc0fd5bd193",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "How do I find a reliable remote full stack development team?",
"searchEngine": "Google",
"topic": "Remote Full Stack Team",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"Remote Full Stack Team\", \"prompt\": \"How do I find a reliable remote full stack development team?\", \"promptId\": \"PROMPT-005\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.18922Z"
},
{
"id": "f06e146e-567b-4953-be84-250019704f63",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "What are quality assurance services and why are they important?",
"searchEngine": "Google",
"topic": "Quality Assurance Services",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"Quality Assurance Services\", \"prompt\": \"What are quality assurance services and why are they important?\", \"promptId\": \"PROMPT-006\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189224Z"
},
{
"id": "21c0a32a-aa28-449d-ae43-f55b74cc18b3",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "What technologies are best for SaaS product development?",
"searchEngine": "Google",
"topic": "SaaS App Development",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"SaaS App Development\", \"prompt\": \"What technologies are best for SaaS product development?\", \"promptId\": \"PROMPT-007\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189226Z"
},
{
"id": "fa3d1c55-3c97-48cd-8ee5-764704813e61",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "How can AI engineering enhance product functionality?",
"searchEngine": "Google",
"topic": "AI Engineering",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"AI Engineering\", \"prompt\": \"How can AI engineering enhance product functionality?\", \"promptId\": \"PROMPT-008\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189232Z"
}
]
}

http://localhost:8088/api/onboarding/analyze-visibility

payload :{
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd"
}

response: {
"success": true,
"error": null,
"totalPromptsAnalyzed": 222,
"prompts": [
{
"id": "e04faa81-c96f-4c69-8325-432c71854385",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "What are the benefits of generative AI development for startups?",
"searchEngine": "Google",
"topic": "Generative AI Development",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"Generative AI Development\", \"prompt\": \"What are the benefits of generative AI development for startups?\", \"promptId\": \"PROMPT-001\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.18893Z"
},
{
"id": "78afcf05-7d1e-4097-b377-eb7679be8e33",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "How can SaaS app development improve my business operations?",
"searchEngine": "Google",
"topic": "SaaS App Development",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"SaaS App Development\", \"prompt\": \"How can SaaS app development improve my business operations?\", \"promptId\": \"PROMPT-002\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189192Z"
},
{
"id": "b5ea64f4-48b3-4634-8fff-f665a5bdda69",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "What features should I consider in a product engineering service?",
"searchEngine": "Google",
"topic": "Product Engineering",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"Product Engineering\", \"prompt\": \"What features should I consider in a product engineering service?\", \"promptId\": \"PROMPT-003\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189204Z"
},
{
"id": "d926d15e-b870-4f6a-919c-8353d0798cdd",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "Can cloud solutions help streamline my IT infrastructure?",
"searchEngine": "Google",
"topic": "Cloud & DevOps Solutions",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"Cloud & DevOps Solutions\", \"prompt\": \"Can cloud solutions help streamline my IT infrastructure?\", \"promptId\": \"PROMPT-004\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189216Z"
},
{
"id": "08f45df8-dff8-4161-a913-fcc0fd5bd193",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "How do I find a reliable remote full stack development team?",
"searchEngine": "Google",
"topic": "Remote Full Stack Team",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"Remote Full Stack Team\", \"prompt\": \"How do I find a reliable remote full stack development team?\", \"promptId\": \"PROMPT-005\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.18922Z"
},
{
"id": "f06e146e-567b-4953-be84-250019704f63",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "What are quality assurance services and why are they important?",
"searchEngine": "Google",
"topic": "Quality Assurance Services",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"Quality Assurance Services\", \"prompt\": \"What are quality assurance services and why are they important?\", \"promptId\": \"PROMPT-006\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189224Z"
},
{
"id": "21c0a32a-aa28-449d-ae43-f55b74cc18b3",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"queryString": "What technologies are best for SaaS product development?",
"searchEngine": "Google",
"topic": "SaaS App Development",
"intent": null,
"difficulty": null,
"persona": null,
"commercialValue": 0,
"monthlySearchEstimate": null,
"region": null,
"language": null,
"topicValidation": null,
"buyerJourneyStage": null,
"isEnriched": false,
"enrichedAt": null,
"rawJson": "{\"topic\": \"SaaS App Development\", \"prompt\": \"What technologies are best for SaaS product development?\", \"promptId\": \"PROMPT-007\"}",
"visibilityScore": 0,
"estimatedRank": null,
"confidence": 0,
"appearsInAnswer": false,
"shareOfVoiceContribution": 0,
"mentionProbability": 0,
"brandStrength": 0,
"contentStrength": 0,
"citationStrength": 0,
"visibilityReason": null,
"generatedAt": "2026-07-02T06:15:49.189226Z"
}
]
}

http://localhost:8088/api/onboarding/analyze-platform-visibility

payload :{
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd"
}

response:{
"success": true,
"error": null,
"platformsAnalyzed": 9,
"summary": {
"id": "0efabbc9-775a-44e4-928f-ca5cf3f1438c",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"overallVisibilityScore": 21,
"bestPlatform": "Meta AI",
"weakestPlatform": "Perplexity",
"averageMentionRate": 22,
"averagePromptCoverage": 24,
"createdAt": "2026-07-02T06:15:49.622814Z"
},
"platformScores": [
{
"id": "e6d9aa45-02b5-4d1c-b41f-b2a14a7bacf3",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"platform": "Perplexity",
"visibilityScore": 20,
"averageRank": "21–50",
"mentionRate": 19,
"promptCoverage": 20,
"confidence": 90,
"strengthsJson": "[\"Clear service-oriented content that aligns with user queries.\",\"Good website structure and user-friendly navigation facilitate content discovery.\",\"Strong emphasis on collaboration and tailored solutions enhances perceived value.\"]",
"weaknessesJson": "[\"Limited citations and backlinks weaken overall authority on Perplexity.\",\"Underdeveloped content categories may affect topic depth and engagement.\",\"Medium brand recognition impacts trust and visibility in search results.\"]",
"explanation": "Ioweb3 Technology shows promise with structured content but needs to boost citations and specialized content for stronger Perplexity performance.",
"isEnriched": true,
"createdAt": "2026-07-02T06:15:49.620275Z"
},
{
"id": "35bd5a54-ef59-4ade-8271-1cff524fa97b",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"platform": "Gemini",
"visibilityScore": 21,
"averageRank": "21–50",
"mentionRate": 23,
"promptCoverage": 25,
"confidence": 90,
"strengthsJson": "[\"Good overall site structure enhances user experience.\",\"Professional tone aligns well with target audience expectations.\",\"Diverse core services attract a range of enterprise customers.\"]",
"weaknessesJson": "[\"Limited depth in specialized content may hinder authority.\",\"Potential lack of backlinks affects overall visibility.\",\"Absence of structured data can reduce search engine understanding.\"]",
"explanation": "Ioweb3 demonstrates solid foundational SEO but needs to enhance content depth and structured data for improved Gemini ranking.",
"isEnriched": true,
"createdAt": "2026-07-02T06:15:49.620274Z"
},
{
"id": "8f02522c-094a-45ac-a82f-8bf913945a5a",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"platform": "ChatGPT",
"visibilityScore": 22,
"averageRank": "21–50",
"mentionRate": 24,
"promptCoverage": 25,
"confidence": 90,
"strengthsJson": "[\"Clear service descriptions and a well-defined business model enhance entity recognition on ChatGPT.\",\"High-quality informative content aligns with user intent, improving engagement with target customers.\",\"Unique selling proposition emphasizes collaboration and tailored solutions, enhancing brand differentiation.\"]",
"weaknessesJson": "[\"Limited backlink profile may affect overall visibility and authority on the ChatGPT platform.\",\"Insufficient specialized and case study content reduces topical authority and recognition.\",\"Low average brand and content strength suggests weak presence in conversational contexts.\"]",
"explanation": "Ioweb3 Technology's clear offerings support recognition on ChatGPT but lack backlinks and specialized content for stronger visibility.",
"isEnriched": true,
"createdAt": "2026-07-02T06:15:49.620204Z"
},
{
"id": "226b7384-7225-4f6e-968a-cc38e98f861b",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"platform": "Meta AI",
"visibilityScore": 23,
"averageRank": "21–50",
"mentionRate": 18,
"promptCoverage": 20,
"confidence": 90,
"strengthsJson": "[\"Established B2B focus with a clear value proposition.\",\"Engagement through diverse content categories like blogs and case studies.\",\"Strong technological foundation with relevant service offerings.\"]",
"weaknessesJson": "[\"Limited topical authority due to insufficient specialized content.\",\"Average brand strength and domain authority may affect visibility.\",\"Absence of case studies or testimonials may hinder trust.\"]",
"explanation": "Ioweb3 Technology demonstrates a solid B2B presence on Meta AI, yet lacks specialized content and robust brand recognition for stronger visibility.",
"isEnriched": true,
"createdAt": "2026-07-02T06:15:49.620276Z"
},
{
"id": "8d580966-9966-4343-97f0-d3574e93a459",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"platform": "Google AI Overview",
"visibilityScore": 21,
"averageRank": "21–50",
"mentionRate": 23,
"promptCoverage": 24,
"confidence": 90,
"strengthsJson": "[\"Strong focus on B2B SaaS offerings and technological collaboration.\",\"Good SEO foundation with clear service descriptions enhancing visibility.\",\"Diverse service portfolio addresses various industries, improving topical relevance.\"]",
"weaknessesJson": "[\"Limited structured data implementation may hinder search engine recognition.\",\"Medium domain authority suggests a need for additional backlinking efforts.\",\"Lack of specialized content and case studies reduces topical authority.\"]",
"explanation": "Ioweb3 Technology shows a solid foundation but lacks advanced SEO strategies and authoritative content to boost visibility on Google AI Overview.",
"isEnriched": true,
"createdAt": "2026-07-02T06:15:49.620275Z"
},
{
"id": "63c6a083-5206-45dd-8c24-1cc5f83816f7",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"platform": "Microsoft Copilot",
"visibilityScore": 21,
"averageRank": "21–50",
"mentionRate": 26,
"promptCoverage": 27,
"confidence": 90,
"strengthsJson": "[\"Good integration of relevant AI topics such as SaaS Development and AI Engineering.\",\"Professional and informative tone aligns well with the needs of enterprise clients.\",\"Clear service offerings enhance understanding and usability for users.\"]",
"weaknessesJson": "[\"Limited external citations may hinder visibility and recognition on Copilot.\",\"Lack of specialized content and case studies affects topical authority.\",\"Missing structured data implementation could reduce content discoverability.\"]",
"explanation": "The business shows strong relevance in AI aspects but needs enhanced content depth and visibility for improved ranking on Microsoft Copilot.",
"isEnriched": true,
"createdAt": "2026-07-02T06:15:49.620276Z"
},
{
"id": "c1848e00-667f-463d-a969-ba516576bd01",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"platform": "DeepSeek",
"visibilityScore": 21,
"averageRank": "21–50",
"mentionRate": 26,
"promptCoverage": 28,
"confidence": 90,
"strengthsJson": "[\"Good SEO foundation with clear service descriptions\",\"Robust primary technologies utilized for service delivery\",\"Strong emphasis on collaboration and tailored engineering teams\"]",
"weaknessesJson": "[\"Limited structured data usage impacting search visibility\",\"Low average brand and content strength scores\",\"Medium authority level requiring specialized content enhancement\"]",
"explanation": "DeepSeek metrics indicate a solid SEO base but highlight areas for improvement in content specialization and brand recognition.",
"isEnriched": true,
"createdAt": "2026-07-02T06:15:49.620278Z"
},
{
"id": "1633f54f-596c-4c25-b08e-97442add832b",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"platform": "Grok",
"visibilityScore": 23,
"averageRank": "21–50",
"mentionRate": 23,
"promptCoverage": 24,
"confidence": 90,
"strengthsJson": "[\"Strong emphasis on collaboration and customer-tailored engineering teams\",\"Well-structured website with clear navigation and service descriptions\",\"Diverse range of core services addressing key industry needs\"]",
"weaknessesJson": "[\"Limited content depth and lack of case studies reduce topical authority\",\"Insufficient backlinks hindering overall domain authority\",\"Absence of pricing information might deter potential clients\"]",
"explanation": "Ioweb3 has solid foundations but needs to enhance content depth and backlinks to boost its presence on Grok.",
"isEnriched": true,
"createdAt": "2026-07-02T06:15:49.620281Z"
},
{
"id": "39f6cda5-754b-498d-8c91-60ea86c27c7c",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"platform": "Claude",
"visibilityScore": 22,
"averageRank": "21–50",
"mentionRate": 22,
"promptCoverage": 23,
"confidence": 90,
"strengthsJson": "[\"Well-structured content with clear service descriptions improves comprehension and engagement.\",\"Strong presence of relevant topics like SaaS Development and Cloud Solutions enhances topical authority.\",\"A reliable brand positioning as a technological partner attracts target customers.\"]",
"weaknessesJson": "[\"Limited backlink profile affects visibility and brand recognition on the platform.\",\"Average brand and content strength scores indicate room for improving overall credibility.\",\"Lack of specialized content and case studies may hinder perceived authority.\"]",
"explanation": "The business demonstrates good structure and thematic relevance but lacks strong backlinks and specialized content for enhanced credibility on Claude.",
"isEnriched": true,
"createdAt": "2026-07-02T06:15:49.620274Z"
}
]
}

http://localhost:8088/api/onboarding/analyze-citations

paylaod : {
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd"
}

response: {
"success": true,
"error": null,
"sourcesAnalyzed": 36,
"summary": {
"id": "79fb35b2-0689-41ed-8d2d-4accc635a47a",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"totalSources": 36,
"averageAuthorityScore": 0,
"averageInfluenceScore": 0,
"highestOpportunitySource": "",
"mostInfluentialSource": "",
"createdAt": "2026-07-02T12:31:35.06422"
},
"sources": [
{
"id": "0d3bfe71-5b6f-4cbf-a355-3e08ccc8351c",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"rank": 1,
"source": "https://aws.amazon.com/",
"category": "Official Documentation",
"authorityScore": 0,
"influenceScore": 0,
"citationFrequency": 0,
"competitorCoverage": 0,
"opportunityScore": 0,
"mentionProbability": 0,
"reason": "Leader in cloud solutions, providing extensive resources and documentation for developers.",
"isEnriched": false,
"enrichedAt": null,
"createdAt": "2026-07-02T12:31:35.063712"
},
{
"id": "c95c19d5-0949-4c27-8c8f-a005266626e5",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"rank": 2,
"source": "https://www.microsoft.com/en-us/cloud-platform",
"category": "Official Documentation",
"authorityScore": 0,
"influenceScore": 0,
"citationFrequency": 0,
"competitorCoverage": 0,
"opportunityScore": 0,
"mentionProbability": 0,
"reason": "Comprehensive resources on cloud services and Microsoft's development ecosystem.",
"isEnriched": false,
"enrichedAt": null,
"createdAt": "2026-07-02T12:31:35.063765"
},
{
"id": "96a7c9a1-794f-437c-97c2-09c7df9295dc",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"rank": 3,
"source": "https://www.ibm.com/cloud",
"category": "Official Documentation",
"authorityScore": 0,
"influenceScore": 0,
"citationFrequency": 0,
"competitorCoverage": 0,
"opportunityScore": 0,
"mentionProbability": 0,
"reason": "Offers in-depth documentation and services for cloud and AI solutions.",
"isEnriched": false,
"enrichedAt": null,
"createdAt": "2026-07-02T12:31:35.063766"
},
{
"id": "1b3033e5-63f8-4e16-878c-47bacb8d84cb",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"rank": 4,
"source": "https://developers.google.com/",
"category": "Official Documentation",
"authorityScore": 0,
"influenceScore": 0,
"citationFrequency": 0,
"competitorCoverage": 0,
"opportunityScore": 0,
"mentionProbability": 0,
"reason": "Key resource for developers on Google’s cloud and AI technologies.",
"isEnriched": false,
"enrichedAt": null,
"createdAt": "2026-07-02T12:31:35.063766"
},
{
"id": "daa8e970-a32c-4662-a477-83d626a03605",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"rank": 5,
"source": "https://www.forbes.com/",
"category": "Industry Publications",
"authorityScore": 0,
"influenceScore": 0,
"citationFrequency": 0,
"competitorCoverage": 0,
"opportunityScore": 0,
"mentionProbability": 0,
"reason": "Highly regarded for business and technology articles influencing industry trends.",
"isEnriched": false,
"enrichedAt": null,
"createdAt": "2026-07-02T12:31:35.063766"
},
{
"id": "ae407826-9bf3-4aec-8b13-ef72a9b41953",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"rank": 6,
"source": "https://www.techcrunch.com/",
"category": "Technology News",
"authorityScore": 0,
"influenceScore": 0,
"citationFrequency": 0,
"competitorCoverage": 0,
"opportunityScore": 0,
"mentionProbability": 0,
"reason": "Significant coverage of startups and technology advancements impacting SaaS development.",
"isEnriched": false,
"enrichedAt": null,
"createdAt": "2026-07-02T12:31:35.063767"
}
]
}

http://localhost:8088/api/onboarding/analyze-personas

payload : {
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd"
}

{
"success": true,
"error": null,
"personasAnalyzed": 10,
"summary": {
"id": "cb5728c3-8e27-42ca-973b-17fc6e53fe3c",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"overallVisibility": 54,
"strongestPersona": "Marketing Director",
"weakestPersona": "Founder",
"averageShareOfVoice": 58,
"createdAt": "2026-07-02T12:31:54.592755"
},
"scores": [
{
"id": "c85ebd7e-486b-4dff-923d-841b003ae32b",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"persona": "Operations Manager",
"visibility": 54,
"averageRank": "21–50",
"shareOfVoice": 55,
"topCompetitorsJson": "[\"Competitor A\",\"Competitor B\",\"Competitor C\",\"Competitor D\",\"Competitor E\"]",
"recommendedContentJson": "[\"Workflow automation guides\",\"Efficiency benchmarks\",\"Operational excellence frameworks\"]",
"reason": "Solid visibility but could use more operational-focused resources.",
"createdAt": "2026-07-02T12:31:54.596696"
},
{
"id": "f7a0c40d-77e5-417b-b173-76591c893297",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"persona": "Marketing Director",
"visibility": 70,
"averageRank": "4–10",
"shareOfVoice": 70,
"topCompetitorsJson": "[\"Competitor A\",\"Competitor B\",\"Competitor C\",\"Competitor D\",\"Competitor E\"]",
"recommendedContentJson": "[\"SEO strategies\",\"GEO content plans\",\"Lead generation guides\"]",
"reason": "Excellent visibility due to comprehensive marketing content.",
"createdAt": "2026-07-02T12:31:54.596686"
},
{
"id": "2a5853b5-da3b-4722-af4e-911c25dfafd3",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"persona": "Enterprise Buyer",
"visibility": 61,
"averageRank": "11–20",
"shareOfVoice": 65,
"topCompetitorsJson": "[\"Competitor A\",\"Competitor B\",\"Competitor C\",\"Competitor D\",\"Competitor E\"]",
"recommendedContentJson": "[\"Vendor credibility reports\",\"Security compliance pages\",\"SLA templates\"]",
"reason": "Strong visibility, focused on enterprise compliance and security.",
"createdAt": "2026-07-02T12:31:54.59668"
},
{
"id": "b5b93912-fef6-4eae-a17a-c583c627bfec",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"persona": "Startup",
"visibility": 49,
"averageRank": "21–50",
"shareOfVoice": 45,
"topCompetitorsJson": "[\"Competitor A\",\"Competitor B\",\"Competitor C\",\"Competitor D\",\"Competitor E\"]",
"recommendedContentJson": "[\"MVP development guides\",\"Fundraising support articles\",\"Success stories\"]",
"reason": "Needs targeted content for startups to increase relevance.",
"createdAt": "2026-07-02T12:31:54.596676"
},
{
"id": "c3dc1f98-1887-4946-a500-09c80300d4a5",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"persona": "Product Manager",
"visibility": 57,
"averageRank": "21–50",
"shareOfVoice": 60,
"topCompetitorsJson": "[\"Competitor A\",\"Competitor B\",\"Competitor C\",\"Competitor D\",\"Competitor E\"]",
"recommendedContentJson": "[\"Product demos\",\"User experience reports\",\"Analytics insights\"]",
"reason": "Strong visibility, focusing on product strategy and analytics.",
"createdAt": "2026-07-02T12:31:54.596672"
},
{
"id": "1d8bc6c2-c020-4e4a-8051-49b5d1716658",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"persona": "Engineering Manager",
"visibility": 53,
"averageRank": "21–50",
"shareOfVoice": 50,
"topCompetitorsJson": "[\"Competitor A\",\"Competitor B\",\"Competitor C\",\"Competitor D\",\"Competitor E\"]",
"recommendedContentJson": "[\"DevOps guides\",\"Testing methodologies\",\"Delivery success stories\"]",
"reason": "Good visibility but should emphasize team productivity topics.",
"createdAt": "2026-07-02T12:31:54.59665"
},
{
"id": "28f5b86a-a148-421c-8191-7000b2fa718e",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"persona": "Developer",
"visibility": 50,
"averageRank": "21–50",
"shareOfVoice": 45,
"topCompetitorsJson": "[\"Competitor A\",\"Competitor B\",\"Competitor C\",\"Competitor D\",\"Competitor E\"]",
"recommendedContentJson": "[\"Code samples\",\"Open-source resources\",\"Documentation updates\"]",
"reason": "Needs more resources catered to developers for higher engagement.",
"createdAt": "2026-07-02T12:31:54.596644"
},
{
"id": "bbe1941f-23a1-4b51-b807-0493eadcc1bc",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"persona": "CTO",
"visibility": 52,
"averageRank": "21–50",
"shareOfVoice": 55,
"topCompetitorsJson": "[\"Competitor A\",\"Competitor B\",\"Competitor C\",\"Competitor D\",\"Competitor E\"]",
"recommendedContentJson": "[\"API documentation\",\"Technical whitepapers\",\"Architecture diagrams\"]",
"reason": "Relevant technical content is needed for better visibility.",
"createdAt": "2026-07-02T12:31:54.596638"
},
{
"id": "1a868be0-d943-4cbd-ab01-917872ba1c44",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"persona": "CEO",
"visibility": 48,
"averageRank": "21–50",
"shareOfVoice": 50,
"topCompetitorsJson": "[\"Competitor A\",\"Competitor B\",\"Competitor C\",\"Competitor D\",\"Competitor E\"]",
"recommendedContentJson": "[\"ROI calculators\",\"Whitepapers\",\"Benchmark reports\"]",
"reason": "Moderate visibility with room for improvement in showcasing ROI.",
"createdAt": "2026-07-02T12:31:54.596629"
},
{
"id": "d1889d59-6065-48ad-91d5-f5fade25c818",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"persona": "Founder",
"visibility": 29,
"averageRank": "21–50",
"shareOfVoice": 40,
"topCompetitorsJson": "[\"Competitor A\",\"Competitor B\",\"Competitor C\",\"Competitor D\",\"Competitor E\"]",
"recommendedContentJson": "[\"Technical blog posts\",\"Case studies\",\"Industry guides\"]",
"reason": "Limited focus on startup-centric content and competitive landscape.",
"createdAt": "2026-07-02T12:31:54.596477"
}
]
}

http://localhost:8088/api/onboarding/analyze-regions

payload : {
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd"
}

{
"success": true,
"error": null,
"summary": {
"id": "bf799d15-cb64-41b8-b307-7c166001d54e",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"overallGlobalVisibility": 63,
"strongestRegion": "USA",
"weakestRegion": "Middle East",
"averageShareOfVoice": 35,
"createdAt": "2026-07-02T12:32:07.225665"
},
"scores": [
{
"id": "f70aa82f-5b3f-4472-b737-95af738a6339",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"region": "Europe",
"visibility": 58,
"ranking": "11–20",
"competitorLeader": "SAP",
"shareOfVoice": 28,
"contentOpportunityJson": "[\"Localized blog articles\",\"Industry reports\",\"Regional comparison pages\"]",
"reason": "Multilingual challenges and varying compliance requirements across markets.",
"createdAt": "2026-07-02T12:32:07.235352"
},
{
"id": "23376656-846f-497b-8e1c-45d2fc960c72",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"region": "Middle East",
"visibility": 55,
"ranking": "11–20",
"competitorLeader": "STC",
"shareOfVoice": 25,
"contentOpportunityJson": "[\"Regional success stories\",\"Industry compliance guides\",\"Local customer testimonials\"]",
"reason": "Emerging market with government focus on digital transformation but lower brand awareness.",
"createdAt": "2026-07-02T12:32:07.235064"
},
{
"id": "015214e5-f34c-4e64-81f9-a5b7d7578b01",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"region": "Singapore",
"visibility": 70,
"ranking": "4–10",
"competitorLeader": "Grab",
"shareOfVoice": 42,
"contentOpportunityJson": "[\"Local partnerships\",\"Localized blog articles\",\"Regional success stories\"]",
"reason": "Hub for fintech and cloud innovation with strong demand for SaaS solutions.",
"createdAt": "2026-07-02T12:32:07.23484"
},
{
"id": "7decb567-51b4-46c3-b513-4a8672efb77c",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"region": "Australia",
"visibility": 66,
"ranking": "4–10",
"competitorLeader": "Atlassian",
"shareOfVoice": 38,
"contentOpportunityJson": "[\"Local partnerships\",\"Regional comparison pages\",\"Localized blog articles\"]",
"reason": "Growing cloud adoption and modernization among enterprises.",
"createdAt": "2026-07-02T12:32:07.234589"
},
{
"id": "a7ba7f8e-8eaa-43b5-85b1-d3ba9df813d7",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"region": "Germany",
"visibility": 62,
"ranking": "4–10",
"competitorLeader": "SAP",
"shareOfVoice": 32,
"contentOpportunityJson": "[\"Local customer testimonials\",\"Regional case studies\",\"Industry reports\"]",
"reason": "Strong focus on engineering and compliance but complex market.",
"createdAt": "2026-07-02T12:32:07.234177"
},
{
"id": "d0e6bd64-4ff0-40da-b7ef-3a20a16675fb",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"region": "UK",
"visibility": 65,
"ranking": "4–10",
"competitorLeader": "Sage",
"shareOfVoice": 40,
"contentOpportunityJson": "[\"Localized blog articles\",\"Industry compliance guides\",\"Country-specific pricing\"]",
"reason": "High demand for enterprise software solutions and compliance awareness.",
"createdAt": "2026-07-02T12:32:07.232613"
},
{
"id": "feb16fd0-8de4-45f9-b470-f8c6726baac3",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"region": "Canada",
"visibility": 60,
"ranking": "4–10",
"competitorLeader": "Shopify",
"shareOfVoice": 35,
"contentOpportunityJson": "[\"Local landing pages\",\"Industry reports\",\"Regional comparison pages\"]",
"reason": "Growth in digital transformation initiatives among enterprises.",
"createdAt": "2026-07-02T12:32:07.23233"
},
{
"id": "5c3b1943-1642-46ad-bde4-744b51cdb2e2",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"region": "India",
"visibility": 68,
"ranking": "4–10",
"competitorLeader": "Tata Consultancy Services",
"shareOfVoice": 30,
"contentOpportunityJson": "[\"Localized blog articles\",\"Regional success stories\",\"Country-specific FAQs\"]",
"reason": "Strong demand for IT services and SaaS but high competition from established firms.",
"createdAt": "2026-07-02T12:32:07.231973"
},
{
"id": "422ee1b3-4540-4a70-8a3f-b83a616958a6",
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd",
"region": "USA",
"visibility": 75,
"ranking": "4–10",
"competitorLeader": "Salesforce",
"shareOfVoice": 45,
"contentOpportunityJson": "[\"Regional case studies\",\"Local customer testimonials\",\"Industry compliance guides\",\"Local partnerships\"]",
"reason": "High brand recognition and mature market with competitive SaaS landscape.",
"createdAt": "2026-07-02T12:32:07.229009"
}
]
}

http://localhost:8088/api/onboarding/generate-recommendations

payload : {
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd"
}

response : {
"success": true,
"error": null,
"summary": {
"overallPriority": "Critical",
"estimatedOverallImpact": "Very High",
"estimatedImplementationTime": "3-6 months",
"totalRecommendations": 33,
"criticalRecommendations": 15,
"highPriorityRecommendations": 0
},
"recommendations": [
{
"recommendationId": "7203b591-09e3-4bc8-ab35-d3dffc43c5ff",
"category": "Brand Authority Improvements",
"title": "Create a Knowledge Base Resource",
"description": "Develop a knowledge base resource on your website that addresses common industry questions, enhancing authority and usability.",
"priority": "Medium",
"estimatedImpact": "Very High",
"estimatedDifficulty": "Very Difficult",
"implementationTime": "3-6 months",
"expectedOutcome": "Higher brand trust and likelihood of being recommended by AI as a solution.",
"successMetric": "Increase AI mention rate",
"actionItems": []
},
{
"recommendationId": "918380d5-4da2-448c-b4aa-f1888597d901",
"category": "Brand Authority Improvements",
"title": "Build Partnerships with Educational Institutions",
"description": "Collaborate with universities or educational institutions to enhance your brand’s authority through research partnerships or student projects.",
"priority": "Medium",
"estimatedImpact": "Very High",
"estimatedDifficulty": "Very Difficult",
"implementationTime": "3-6 months",
"expectedOutcome": "Higher brand trust and likelihood of being recommended by AI as a solution.",
"successMetric": "Increase AI mention rate",
"actionItems": []
},
{
"recommendationId": "1abaa43c-9098-4227-94f3-39b4623a541b",
"category": "Brand Authority Improvements",
"title": "Publish Whitepapers on Industry Trends",
"description": "Create and distribute whitepapers that analyze current trends to establish authority in the AI and SaaS space, attracting backlinks and mentions.",
"priority": "Medium",
"estimatedImpact": "Very High",
"estimatedDifficulty": "Very Difficult",
"implementationTime": "3-6 months",
"expectedOutcome": "Higher brand trust and likelihood of being recommended by AI as a solution.",
"successMetric": "Increase AI mention rate",
"actionItems": []
},
{
"recommendationId": "911add0f-9728-4490-a796-54d62a74d562",
"category": "Brand Authority Improvements",
"title": "Collaborate with Influencers",
"description": "Identify and collaborate with industry influencers to reach a wider audience and build brand authority through trusted endorsements.",
"priority": "Medium",
"estimatedImpact": "Very High",
"estimatedDifficulty": "Very Difficult",
"implementationTime": "3-6 months",
"expectedOutcome": "Higher brand trust and likelihood of being recommended by AI as a solution.",
"successMetric": "Increase AI mention rate",
"actionItems": []
},
{
"recommendationId": "aeb97d18-a71b-4bd0-88fa-7b06445a21cc",
"category": "Brand Authority Improvements",
"title": "Engage in Industry Webinars",
"description": "Host or participate in webinars discussing industry trends to position Ioweb3 as a thought leader and enhance brand visibility.",
"priority": "Medium",
"estimatedImpact": "Very High",
"estimatedDifficulty": "Very Difficult",
"implementationTime": "3-6 months",
"expectedOutcome": "Higher brand trust and likelihood of being recommended by AI as a solution.",
"successMetric": "Increase AI mention rate",
"actionItems": []
},
{
"recommendationId": "ab0925db-a291-467f-a60b-c4b514abfdfb",
"category": "Content Improvements",
"title": "Create Pillar Pages for Core Services",
"description": "Develop pillar pages that provide an overview of each core service, linking to detailed subpages to enhance content depth and structure.",
"priority": "Medium",
"estimatedImpact": "High",
"estimatedDifficulty": "Difficult",
"implementationTime": "2-4 weeks",
"expectedOutcome": "Better alignment with user intent for unserved personas.",
"successMetric": "Improve Prompt Coverage",
"actionItems": []
},
{
"recommendationId": "67ae9456-538d-4c3b-a78a-0900d70494ac",
"category": "Prompt Coverage Improvements",
"title": "Develop Interactive Tools",
"description": "Create interactive tools or calculators relevant to your service offerings to engage users and improve visibility on AI platforms.",
"priority": "Medium",
"estimatedImpact": "High",
"estimatedDifficulty": "Difficult",
"implementationTime": "2-4 weeks",
"expectedOutcome": "Better alignment with user intent for unserved personas.",
"successMetric": "Improve Prompt Coverage",
"actionItems": []
},
{
"recommendationId": "4dbd2660-211f-4c18-a40b-4d4539780a16",
"category": "Content Improvements",
"title": "Host a Regular Podcast Series",
"description": "Launch a podcast series discussing relevant topics in the technology and AI spaces to attract a new audience and improve brand visibility.",
"priority": "Medium",
"estimatedImpact": "High",
"estimatedDifficulty": "Difficult",
"implementationTime": "2-4 weeks",
"expectedOutcome": "Better alignment with user intent for unserved personas.",
"successMetric": "Improve Prompt Coverage",
"actionItems": []
}
]
}

http://localhost:8088/api/onboarding/generate-executive-summary

payload: {
"organizationId": "1a6e127b-3d45-4ada-948a-8af6633ee0fd"
}

response:{
"success": true,
"error": null,
"data": {
"businessOverview": "Ioweb3 Technology is a B2B SaaS provider specializing in software engineering, offering product development, cloud and DevOps solutions, and quality assurance services. The company focuses on building and scaling digital products for startups and enterprises, leveraging modern technology stacks. Target customers include enterprise IT managers and startups across various industries such as fintech, healthcare, and e-commerce.",
"currentAIVisibility": "Ioweb3 has a limited AI platform presence with a global search visibility score of 21 and an estimated brand mention rate of 22. This indicates potential underperformance in AI-driven interactions and a need for increased engagement across multiple AI platforms.",
"competitorPosition": "Ioweb3 holds a medium market position with 50 identified competitors. The company's competitive strengths include clear service offerings and user-friendly navigation. However, it lags in brand recognition and citation authority compared to top competitors.",
"platformPerformance": "Ioweb3's performance across major AI platforms shows varying strengths, with the strongest visibility noted on platforms like Google AI and Microsoft Copilot. The weakest performance was observed on platforms like DeepSeek and Grok, indicating an opportunity for targeted improvement.",
"topicPerformance": "Ioweb3 demonstrates medium visibility in key topics like SaaS development and AI engineering, with significant gaps in areas such as cloud solutions and technology consulting. Strengthening content in these topics could enhance overall visibility.",
"promptPerformance": "The company has a wide coverage of prompts with a total of 222 generated. High-performing prompts focus on generative AI and SaaS solutions, while lower-performing prompts reveal a need for improved customer intent coverage.",
"citationSummary": "Ioweb3 currently has an overall citation authority of 0, highlighting a critical gap in brand visibility. The business lacks significant citations and should focus on building relationships with key sources to improve its authority within the industry.",
"strengths": [
"Strong technical expertise",
"High topical authority",
"Quality service pages",
"Collaborative focus on engineering solutions",
"User-friendly website navigation"
],
"weaknesses": [
"Limited brand recognition",
"Insufficient citations",
"Weak AI platform engagement",
"Need for deeper content on specific services",
"Lack of structured data implementation"
],
"opportunities": [
"Enhance structured data usage for better SEO",
"Increase content depth via specialized articles and case studies",
"Leverage AI platforms for broader reach",
"Capture missing citations to enhance authority",
"Address internal linking gaps for improved SEO performance"
],
"threats": [
"Strong competitors with higher brand awareness",
"Rapidly evolving AI search landscape",
"Potential regional competition",
"Limited market share in enterprise solutions",
"Dependence on technology trends without diversifying offerings"
],
"scores": {
"overallGEOScore": 65,
"overallAIVisibilityScore": 21,
"overallSEOScore": 75,
"overallBrandAuthority": 50,
"overallContentScore": 70
},
"executiveSummary": {
"overallAssessment": "Ioweb3 Technology has established a solid foundation in SEO and offers a range of relevant services, but it faces significant challenges in AI visibility and brand authority. Immediate focus on enhancing citation strength and AI engagement will be crucial.",
"topPriorityRecommendation": "Prioritize improving citation authority to raise brand visibility and recognition in target markets.",
"expectedBusinessImpact": "Enhancing citation strength could significantly increase brand authority, leading to improved customer acquisition and engagement, ultimately driving revenue growth.",
"nextSteps": [
"Implement structured data across the website",
"Develop and publish specialized content and case studies",
"Engage with industry publications for citation opportunities",
"Monitor performance on key AI platforms",
"Review internal linking strategy for better SEO outcomes"
]
}
}

---
### GET /api/Dashboard/geo-dashboard
Fetches all real-time data required for the GEO Dashboard, including current metrics, changes, historical trends, and share of voice.

Query Parameters:
- `organizationId` (Guid, Required): The organization ID.

Example Request:
`GET http://localhost:8088/api/Dashboard/geo-dashboard?organizationId=1a6e127b-3d45-4ada-948a-8af6633ee0fd`

Example Response:
```json
{
  "scores": {
    "visibilityScore": { "value": 78, "change": "+5.2%", "direction": "up" },
    "citationScore": { "value": 82, "change": "+3.1%", "direction": "up" },
    "sentimentScore": { "value": 65, "change": "-1.2%", "direction": "down" },
    "competitorScore": { "value": 71, "change": "+2.5%", "direction": "up" },
    "hallucinationRisk": { "value": 12, "change": "-2.0%", "direction": "down" },
    "seoHealth": { "value": 91, "change": "+0.5%", "direction": "up" },
    "aeoReadiness": { "value": 68, "change": "+8.4%", "direction": "up" },
    "geoReadiness": { "value": 74, "change": "+4.1%", "direction": "up" }
  },
  "trend": [
    { "day": 1, "score": 120 },
    { "day": 2, "score": 122 }
  ],
  "shareOfVoice": [
    { "name": "Brand A", "value": 38.4, "color": "#6366F1" },
    { "name": "Brand B", "value": 26.1, "color": "#16A34A" }
  ]
}
```
