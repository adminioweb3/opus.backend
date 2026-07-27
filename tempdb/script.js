const { Client } = require('pg');
const client = new Client({
  user: 'postgres',
  host: 'localhost',
  database: 'opus_db',
  password: 'postgres',
  port: 5432
});
client.connect();

// Check actual visibility data in the DB
client.query(`
  SELECT t.name AS topic, q.prompttext AS prompt, a.status, a.runat, v.overallvisibilityscore, v.mentionfrequency, v.averageposition, v.shareofvoice, v.citationcount
  FROM promptvisibility v
  JOIN promptanalysis a ON v.promptanalysisid = a.id
  JOIN promptquestions q ON a.promptquestionid = q.id
  JOIN prompttopics t ON q.prompttopicid = t.id
  WHERE a.status = 'Completed'
  ORDER BY a.runat DESC
  LIMIT 10
`, (err, res) => {
  if (err) { console.log('ERROR:', err.message); }
  else { console.log('Visibility rows:'); res.rows.forEach(r => console.log(JSON.stringify(r))); }
  
  // Also check what brand is stored
  client.query('SELECT businessname, websiteurl FROM websiteprofiles LIMIT 3', (err2, res2) => {
    if (err2) { console.log('Profile ERROR:', err2.message); }
    else { console.log('Profiles:', res2.rows); }
    
    // Check prompt responses for the latest analysis
    client.query(`
      SELECT r.platform, LEFT(r.responsetext, 200) as preview
      FROM promptresponses r
      JOIN promptanalysis a ON r.promptanalysisid = a.id
      ORDER BY a.runat DESC
      LIMIT 5
    `, (err3, res3) => {
      if (err3) { console.log('Response ERROR:', err3.message); }
      else { console.log('Latest responses:'); res3.rows.forEach(r => console.log(r.platform + ':', r.preview)); }
      client.end();
    });
  });
});
