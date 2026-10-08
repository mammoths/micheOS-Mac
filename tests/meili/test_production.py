import importlib.util, tempfile, unittest, pathlib, urllib.error, threading, urllib.request, http.cookiejar, json
spec=importlib.util.spec_from_file_location('production',pathlib.Path(__file__).parents[2]/'MeiliStudio/production.py');p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)
class ProductionTests(unittest.TestCase):
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.assets=pathlib.Path(__file__).parents[2]/'MeiliStudio';self.s=p.Studio(self.tmp.name,self.assets);p.atomic(self.s.root/'fal-secret.json',{'key':'test-secret'});self.calls=0
  def request(url,payload=None):
   self.calls+=1;return {'request_id':'test-request','status_url':'https://queue.fal.run/test/status','response_url':'https://queue.fal.run/test/result'}
  self.s.request=request;self.c=self.s.state['concepts'][0];self.c['reference']='references/test.png';(self.s.root/'references').mkdir();(self.s.root/'references/test.png').write_bytes((pathlib.Path(__file__).parents[1]/'fixtures/vision.png').read_bytes())
 def tearDown(self):self.tmp.cleanup()
 def test_idempotency(self):
  one=self.s.submit(self.c['id'],'still','same');two=self.s.submit(self.c['id'],'still','same');self.assertEqual(one['id'],two['id']);self.assertEqual(self.calls,1)
 def test_blocks_duplicate_active_job(self):
  self.s.submit(self.c['id'],'still','first')
  with self.assertRaises(ValueError):self.s.submit(self.c['id'],'still','second')
 def test_video_needs_approval(self):
  with self.assertRaises(ValueError):self.s.submit(self.c['id'],'video','first')
  self.assertEqual(self.calls,0)
 def test_budget_enforced_before_spending(self):
  self.s.state['budget']=0.01
  with self.assertRaises(ValueError):self.s.submit(self.c['id'],'still','first')
  self.assertEqual(self.calls,0)
 def test_unknown_submission_never_retried(self):
  def broken(*args):raise TimeoutError()
  self.s.request=broken;j=self.s.submit(self.c['id'],'still','same');self.assertEqual(j['status'],'Unknown submission')
  restored=p.Studio(self.tmp.name,self.assets);self.assertEqual(restored.state['jobs'][0]['status'],'Unknown submission')
 def test_credentials_are_private_and_not_exported(self):
  self.assertNotIn('test-secret',json.dumps(self.s.public()));self.assertEqual((self.s.root/'fal-secret.json').stat().st_mode & 0o777,0o600)
 def test_path_escape_rejected(self):
  with self.assertRaises(ValueError):self.s.reference('../fal-secret.json')
 def test_http_auth_and_origin(self):
  server=p.http.server.ThreadingHTTPServer(('127.0.0.1',0),p.Handler);server.studio=self.s;threading.Thread(target=server.serve_forever,daemon=True).start();origin='http://127.0.0.1:'+str(server.server_port)
  try:
   with self.assertRaises(urllib.error.HTTPError):urllib.request.urlopen(origin+'/api/state')
   jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar));client.open(origin+'/boot?token='+self.s.token).read()
   self.assertTrue(json.load(client.open(origin+'/api/state'))['connected'])
   req=urllib.request.Request(origin+'/api/settings',data=b'{"budget":1}',headers={'Content-Type':'application/json','Origin':'https://wrong.example'})
   with self.assertRaises(urllib.error.HTTPError):client.open(req)
   with self.assertRaises(urllib.error.HTTPError):client.open(origin+'/media/fal-secret.json')
  finally:server.shutdown();server.server_close()
if __name__=='__main__':unittest.main()
