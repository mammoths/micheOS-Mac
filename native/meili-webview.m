#import <AppKit/AppKit.h>
#import <WebKit/WebKit.h>
#import <objc/runtime.h>

typedef void (*MeiliCallback)(const char *);
@interface MeiliBridge : NSObject<WKScriptMessageHandler,WKNavigationDelegate,WKUIDelegate>
@property(nonatomic,weak) WKWebView *view;
@property(nonatomic,strong) NSURL *assetRoot;
@property(nonatomic,strong) NSURL *draftURL;
@property(nonatomic,strong) NSString *localOrigin;
@property(nonatomic,assign) MeiliCallback callback;
@end
@implementation MeiliBridge
 - (void)webView:(WKWebView *)view runJavaScriptConfirmPanelWithMessage:(NSString *)message initiatedByFrame:(WKFrameInfo *)frame completionHandler:(void (^)(BOOL))completion {
    NSAlert *alert=[NSAlert new];alert.messageText=message;[alert addButtonWithTitle:@"Continue"];[alert addButtonWithTitle:@"Cancel"];
    [alert beginSheetModalForWindow:view.window completionHandler:^(NSModalResponse response){completion(response==NSAlertFirstButtonReturn);}];
}
- (void)report:(NSString *)text { if (_callback) _callback(text.UTF8String); }
- (BOOL)save:(NSString *)text {
    NSData *data=[text dataUsingEncoding:NSUTF8StringEncoding];
    if (data.length>2000000) { [self report:@"Project is too large to save. Export a smaller project."]; return NO; }
    NSDictionary *p=[NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    if (![p isKindOfClass:NSDictionary.class] || ![p[@"version"] isEqual:@1] || ![p[@"shots"] isKindOfClass:NSArray.class]) { [self report:@"Invalid project. Your previous save is intact."]; return NO; }
    NSError *error=nil;
    if (![data writeToURL:_draftURL options:NSDataWritingAtomic error:&error]) { [self report:[@"Could not save Meili project: " stringByAppendingString:error.localizedDescription]]; return NO; }
    [self report:@"Meili project saved on this Mac."]; return YES;
}
- (void)userContentController:(WKUserContentController *)controller didReceiveScriptMessage:(WKScriptMessage *)message {
    if (!message.frameInfo.isMainFrame || ![message.body isKindOfClass:NSString.class]) return;
    NSDictionary *body=[NSJSONSerialization JSONObjectWithData:[message.body dataUsingEncoding:NSUTF8StringEncoding] options:0 error:nil];
    NSString *action=body[@"action"]; NSString *text=body[@"text"];
    if ([action isEqual:@"draft"] && [text isKindOfClass:NSString.class]) {
        BOOL ok=[self save:text];[self.view evaluateJavaScript:ok?@"window.meiliSaveStatus(true,'Saved on this Mac')":@"window.meiliSaveStatus(false,'Could not save. Keep this widget open and export your project.')" completionHandler:nil];
    }
    else if ([action isEqual:@"copy"] && [text isKindOfClass:NSString.class]) { [NSPasteboard.generalPasteboard clearContents]; [NSPasteboard.generalPasteboard setString:text forType:NSPasteboardTypeString]; }
    else if ([action isEqual:@"export"] && [text isKindOfClass:NSString.class]) {
        NSSavePanel *panel=NSSavePanel.savePanel;
        NSString *name=body[@"name"];panel.nameFieldStringValue=[name isKindOfClass:NSString.class]?name.lastPathComponent:@"meili-project.json";
        [panel beginSheetModalForWindow:_view.window completionHandler:^(NSModalResponse response) {
            if(response==NSModalResponseOK) { NSError *error=nil; if(![text writeToURL:panel.URL atomically:YES encoding:NSUTF8StringEncoding error:&error]) [self report:error.localizedDescription]; else [self report:@"Meili export saved."]; }
        }];
    } else if ([action isEqual:@"open"]) {
        NSOpenPanel *panel=NSOpenPanel.openPanel;panel.allowsMultipleSelection=NO;panel.canChooseDirectories=NO;
        [panel beginSheetModalForWindow:_view.window completionHandler:^(NSModalResponse response) {
            if(response!=NSModalResponseOK) return;
            NSError *error=nil; NSData *data=[NSData dataWithContentsOfURL:panel.URL options:0 error:&error];
            if (!data || data.length>2000000) { [self report:@"Unable to open this project file. Your draft is intact."]; return; }
            NSString *text=[[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
            NSData *encoded=[NSJSONSerialization dataWithJSONObject:@[text?:@""] options:0 error:nil];
            NSString *arg=[[NSString alloc] initWithData:encoded encoding:NSUTF8StringEncoding];
            [self.view evaluateJavaScript:[NSString stringWithFormat:@"window.meiliOpenText(%@[0])",arg] completionHandler:nil];
        }];
    }
}
- (void)webView:(WKWebView *)webView decidePolicyForNavigationAction:(WKNavigationAction *)navigation decisionHandler:(void (^)(WKNavigationActionPolicy))decisionHandler {
    NSURL *url=navigation.request.URL;
    if (_localOrigin && [[NSString stringWithFormat:@"%@://%@:%@",url.scheme,url.host,url.port] isEqual:_localOrigin]) { decisionHandler(WKNavigationActionPolicyAllow); return; }
    if (url.isFileURL && [url.URLByStandardizingPath.path hasPrefix:[_assetRoot.path stringByAppendingString:@"/"]]) { decisionHandler(WKNavigationActionPolicyAllow); return; }
    if (navigation.navigationType==WKNavigationTypeLinkActivated && ([url.scheme isEqual:@"https"]||[url.scheme isEqual:@"http"])) [NSWorkspace.sharedWorkspace openURL:url];
    decisionHandler(WKNavigationActionPolicyCancel);
}
- (void)webView:(WKWebView *)webView didFailProvisionalNavigation:(WKNavigation *)navigation withError:(NSError *)error { [self report:error.localizedDescription]; }
@end

// The bridge is retained by WebKit's controller; it holds the view weakly.
void *meili_create(const char *assets,const char *directory,const char *startUrl,MeiliCallback callback) {
    @autoreleasepool {
        NSURL *root=[NSURL fileURLWithPath:@(assets) isDirectory:YES];
        NSURL *folder=[NSURL fileURLWithPath:@(directory) isDirectory:YES];
        NSError *error=nil; if(![NSFileManager.defaultManager createDirectoryAtURL:folder withIntermediateDirectories:YES attributes:nil error:&error]) { if(callback)callback(error.localizedDescription.UTF8String); return NULL; }
        MeiliBridge *bridge=[MeiliBridge new];bridge.assetRoot=root;bridge.draftURL=[folder URLByAppendingPathComponent:@"current-project.json"];bridge.callback=callback;
        WKWebViewConfiguration *config=[WKWebViewConfiguration new];
        [config.userContentController addScriptMessageHandler:bridge name:@"meili"];
        if ([NSFileManager.defaultManager fileExistsAtPath:bridge.draftURL.path]) {
            NSData *data=[NSData dataWithContentsOfURL:bridge.draftURL];
            id p=data.length<=2000000?[NSJSONSerialization JSONObjectWithData:data options:0 error:nil]:nil;
            if([p isKindOfClass:NSDictionary.class]) {
                NSString *json=[[NSString alloc]initWithData:data encoding:NSUTF8StringEncoding];
                [config.userContentController addUserScript:[[WKUserScript alloc] initWithSource:[@"window.__micheMeiliSaved=" stringByAppendingString:json] injectionTime:WKUserScriptInjectionTimeAtDocumentStart forMainFrameOnly:YES]];
            } else { if(callback)callback("Saved Meili project could not be read. It has been preserved.");return NULL; }
        }
        WKWebView *view=[[WKWebView alloc]initWithFrame:NSMakeRect(0,0,980,720) configuration:config];bridge.view=view;view.navigationDelegate=bridge;view.UIDelegate=bridge;
        objc_setAssociatedObject(view,@selector(meiliBridge),bridge,OBJC_ASSOCIATION_RETAIN_NONATOMIC);
        if(startUrl && strlen(startUrl)>0) {
            NSURL *url=[NSURL URLWithString:@(startUrl)];bridge.localOrigin=[NSString stringWithFormat:@"%@://%@:%@",url.scheme,url.host,url.port];[view loadRequest:[NSURLRequest requestWithURL:url]];
        } else [view loadFileURL:[root URLByAppendingPathComponent:@"index.html"] allowingReadAccessToURL:root];
        return (__bridge_retained void *)view;
    }
}
void meili_release(void *handle) {
    WKWebView *view=(__bridge_transfer WKWebView *)handle;[view stopLoading];[view.configuration.userContentController removeScriptMessageHandlerForName:@"meili"];view.navigationDelegate=nil;view.UIDelegate=nil;[view removeFromSuperview];
}
void meili_flush(void *handle,MeiliCallback done) {
    WKWebView *view=(__bridge WKWebView *)handle;
    MeiliBridge *bridge=objc_getAssociatedObject(view,@selector(meiliBridge));
    [view evaluateJavaScript:@"window.meiliWorkflow ? (window.meiliWorkflowSaved ? 'workflow-saved' : null) : (window.__micheMeiliLoadFailed ? null : JSON.stringify(window.meiliProjectSnapshot()))" completionHandler:^(id result,NSError *error) {
        BOOL ok=!error && [result isKindOfClass:NSString.class] && ([result isEqual:@"workflow-saved"] || [bridge save:result]);
        if(done)done(ok?"ok":(error?error.localizedDescription.UTF8String:"Meili project could not save. Keep the widget open and retry."));
    }];
}
