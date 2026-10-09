#import <AppKit/AppKit.h>
#import <WebKit/WebKit.h>
#import <objc/runtime.h>
typedef void (*PageCallback)(const char *);
@interface PageBridge:NSObject<WKScriptMessageHandler,WKNavigationDelegate>
@property(nonatomic,weak) WKWebView *view;
@property(nonatomic,strong) NSURL *assetRoot;
@property(nonatomic,assign) PageCallback callback;
@end
@implementation PageBridge
- (void)userContentController:(WKUserContentController *)controller didReceiveScriptMessage:(WKScriptMessage *)message {
 if(!message.frameInfo.isMainFrame||![message.body isKindOfClass:NSString.class])return;
 NSString *text=message.body;if([text lengthOfBytesUsingEncoding:NSUTF8StringEncoding]>16000000)return;
 NSDictionary *body=[NSJSONSerialization JSONObjectWithData:[text dataUsingEncoding:NSUTF8StringEncoding] options:0 error:nil];
 if(![body isKindOfClass:NSDictionary.class])return;
 if([body[@"action"] isEqual:@"pickImage"]){NSOpenPanel *p=NSOpenPanel.openPanel;p.allowsMultipleSelection=NO;p.canChooseDirectories=NO;[p beginSheetModalForWindow:_view.window completionHandler:^(NSModalResponse response){if(response!=NSModalResponseOK)return;NSDictionary *attrs=[NSFileManager.defaultManager attributesOfItemAtPath:p.URL.path error:nil];if([attrs[NSFileSize] unsignedLongLongValue]>10000000){if(self.callback)self.callback("{\"action\":\"error\",\"message\":\"Choose an image under 10 MB.\"}");return;}NSData *data=[NSData dataWithContentsOfURL:p.URL];if(!data)return;NSData *json=[NSJSONSerialization dataWithJSONObject:@{@"action":@"image",@"data":[data base64EncodedStringWithOptions:0]} options:0 error:nil];if(self.callback)self.callback([[NSString alloc]initWithData:json encoding:NSUTF8StringEncoding].UTF8String);}];return;}
 if(_callback)_callback(text.UTF8String);
}
- (void)webView:(WKWebView *)view decidePolicyForNavigationAction:(WKNavigationAction *)action decisionHandler:(void (^)(WKNavigationActionPolicy))done {
 NSURL *url=action.request.URL;
 // Only the bundled editor can navigate; no remote page receives the native bridge.
 if(url.isFileURL&&[url.URLByStandardizingPath.path isEqual:[_assetRoot URLByAppendingPathComponent:@"index.html"].path]){done(WKNavigationActionPolicyAllow);return;}done(WKNavigationActionPolicyCancel);
}
@end
void *page_create(const char *assets,PageCallback callback){
 @autoreleasepool{PageBridge *bridge=[PageBridge new];bridge.assetRoot=[NSURL fileURLWithPath:@(assets) isDirectory:YES];bridge.callback=callback;WKWebViewConfiguration *config=[WKWebViewConfiguration new];[config.userContentController addScriptMessageHandler:bridge name:@"page"];WKWebView *view=[[WKWebView alloc]initWithFrame:NSMakeRect(0,0,440,300) configuration:config];bridge.view=view;view.navigationDelegate=bridge;objc_setAssociatedObject(view,@selector(pageBridge),bridge,OBJC_ASSOCIATION_RETAIN_NONATOMIC);[view loadFileURL:[bridge.assetRoot URLByAppendingPathComponent:@"index.html"] allowingReadAccessToURL:bridge.assetRoot];return (__bridge_retained void *)view;}
}
void page_release(void *handle){WKWebView *view=(__bridge_transfer WKWebView *)handle;[view stopLoading];[view.configuration.userContentController removeScriptMessageHandlerForName:@"page"];view.navigationDelegate=nil;[view removeFromSuperview];}
void page_eval(void *handle,const char *script){WKWebView *view=(__bridge WKWebView *)handle;[view evaluateJavaScript:@(script) completionHandler:nil];}
void page_flush(void *handle,PageCallback callback){WKWebView *view=(__bridge WKWebView *)handle;[view evaluateJavaScript:@"window.pageSnapshot()" completionHandler:^(id result,NSError *error){if(callback)callback(!error&&[result isKindOfClass:NSString.class]?[result UTF8String]:"error");}];}
