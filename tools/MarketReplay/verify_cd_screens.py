import json, pathlib, sqlite3, sys
root=pathlib.Path(sys.argv[1] if len(sys.argv)>1 else 'outputs/stage-cd-vision-final')
j=json.loads((root/'screenshots.json').read_text(encoding='utf-8-sig'))
rows=j['Results']; checks=[]
def check(condition,name):
    if not condition: raise AssertionError(name)
    checks.append(name)
check([r['Page'] for r in rows]==['Market','Market','Purchase','Purchase','Receipt','Listings','Listings','Sell','Listings'],'9张截图页面类型符合人工标注')
check(len(rows[0]['Quotes'])==0,'空收藏页不生成价格')
expected={'过滤罐':6723,'军用罗盘':7392,'军用水壶':9405,'安全绳':13616,'水手哨':5550,'武器零件':16920,'信号接收器':15200,'信号干扰器':18816,'测距望远镜':13188,'无线对讲机':31284,'伞绳':29880,'炮弹':22914,'电台':23104,'军用单兵工具包':30600,'非线性节点探测器':63735}
quotes={q['ItemName']:q['Price'] for q in rows[1]['Quotes']}
check(quotes==expected,'15条已接受卡片报价与人工读图逐项一致；未取得其余5条报价')
for index,price in [(2,25596),(3,24964)]:
    q=rows[index]['Quotes'][0]
    check(q['ItemName']=='无线对讲机' and q['Price']==price and q['Quantity']==1,f'{index+1:02}.png弹窗商品/价格/数量关联正确')
check(rows[3]['TradeFacts']['PriceChanged'],'价格变化拒绝被识别为正常业务事件')
receipt=rows[4]['TradeFacts']['Receipt']
check(receipt['ItemName']=='无线对讲机' and receipt['UnitPrice']==27887 and receipt['Total']==27887,'购买结果名称和均价、总价均匹配')
check(receipt['Quantity'] is None and not receipt['Complete'],'小号数量未达到置信阈值，不从均价/总价反推数量')
for index,used in [(5,511),(6,511),(7,511),(8,512)]:
    q=rows[index]['TradeFacts']['Quota']
    check(q['Used']==used and q['Limit']==1200 and q['Remaining']==1200-used,f'{index+1:02}.png读取已用额度，换算剩余额度')
sale=rows[7]['TradeFacts']['Sale']
check(sale=={'ItemName':'座钟','Quantity':1,'DisplayedUnitPrice':29349,'Gross':29349,'ManagementFee':880,'SaleFee':2053,'NetAfterSaleFee':27296,'NetAfterAllFees':26416},'出售弹窗分别读取两类费用；两项费用后26416')
check(all(not p['ItemBoxVerified'] for r in rows for p in r['TradeFacts']['Presence']),'仓库名称只标候选，不伪装成物品边界定位或数量')
for f in (root/'evidence').glob('*.json'):
    e=json.loads(f.read_text(encoding='utf-8-sig'));v=e['Result']['Vision']
    check(not e['Screen']['Live'] and v['Width']==2560 and v['Height']==1440 and e['TradeFacts']['FrameId']==v['FrameId'] and e['TradeFacts']['ContentHash']==v['ContentHash'],f'{pathlib.Path(e["Screen"]["Frame"]["ScreenshotPath"]).name}保留原尺寸、截图ID、时间及回放来源')
    for token in v['Regions']+v['Texts']:
        b=token['Bounds']
        assert 0<=token['Confidence']<=1 and b['Valid'] and b['X']+b['Width']<=1.00001 and b['Y']+b['Height']<=1.00001
with sqlite3.connect(root/'market.sqlite') as db:
    check(db.execute("select count(*) from PriceObservations where Source='ActualFill'").fetchone()[0]==0,'截图回放没有伪造成交样本')
    check(db.execute('select count(*) from SimulationLedger').fetchone()[0]==0,'截图不直接修改模拟账本')
    check(db.execute("select count(*) from PriceObservations where Source='OcrObservation' and Replay=0").fetchone()[0]==0,'历史截图没有污染实时行情')
(root/'acceptance.json').write_text(json.dumps({'Passed':True,'Checks':checks,'Scope':'人工标注这9张截图的字段回归，不是独立测试集准确率','LowConfidenceQuantityPreserved':True,'RealTradeTested':False},ensure_ascii=False,indent=2),encoding='utf-8')
print(f'{len(checks)} screenshot acceptance checks passed')
