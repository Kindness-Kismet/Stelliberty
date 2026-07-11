const proxyGrepConfig = [
    { name: "广告拦截", gfw: false, extraProxies: "REJECT", urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/AdvertisingLite/AdvertisingLite_Classical.yaml" },
    { name: "linux.do", gfw: true, payload: "DOMAIN-SUFFIX,linux.do" },
    { name: "ping0.cc", gfw: true, extraProxies: "家宽节点", payload: "DOMAIN-SUFFIX,ping0.cc" },
    { name: "GitHub", gfw: true, urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/GitHub/GitHub.yaml" },
    {
      name: "YouTube", gfw: true, urls: [
        "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/YouTube/YouTube.yaml",
        "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/YouTubeMusic/YouTubeMusic.yaml"
      ]
    },
    { name: "Google", gfw: true, extraProxies: "家宽节点", urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/Google/Google.yaml" },
    { name: "Telegram", gfw: true, urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/Telegram/Telegram.yaml" },
    { name: "openAi", gfw: true, extraProxies: "家宽节点", urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/OpenAI/OpenAI.yaml" },
    { name: "Netflix", gfw: true, urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/Netflix/Netflix.yaml" },
    { name: "Twitter", gfw: true, extraProxies: "家宽节点", urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/Twitter/Twitter.yaml" },
    { name: "TikTok", gfw: true, extraProxies: "家宽节点", urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/TikTok/TikTok.yaml" },
    { name: "Facebook", gfw: true, urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/Facebook/Facebook.yaml" },
    { name: "OneDrive", gfw: false, urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/OneDrive/OneDrive.yaml" },
    { name: "Microsoft", gfw: false, urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/Microsoft/Microsoft.yaml" },
    { name: "Steam", gfw: true, urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@release/rule/Clash/Steam/Steam.yaml" },
    { name: "Cloudflare", gfw: true, urls: "https://cdn.jsdelivr.net/gh/blackmatrix7/ios_rule_script@master/rule/Clash/Cloudflare/Cloudflare.yaml" },
  ]

  function main(config) {
    const proxyProviders = config["proxy-providers"];

    proxies = config.proxies;
    function createRuleProviderUrl(url) {
      return {
        "type": "http",
        "interval": 86400,
        "behavior": "classical",
        "format": "yaml",
        "url": url
      }
    }
    function createPayloadRules(payload, name) {
      const rules = [];
      const payloads = Array.isArray(payload) ? payload : [payload];
      for (const item of payloads) {
        const p = item.split(",");
        let pushIndex = p.length;
        if (p[p.length - 1].toLocaleLowerCase() == "no-resolve") {
          pushIndex--;
        }
        p.splice(pushIndex, 0, name.replaceAll(",", "-"));
        rules.push(p.join(","));
      }
      return rules;
    }
    function createGfwProxyGrep(name, addProxies) {
      addProxies = addProxies ? (Array.isArray(addProxies) ? addProxies : [addProxies]) : [];
      return {
        "name": name,
        "type": "select",
        "proxies": [...addProxies, "普通节点", "DIRECT"],
        "include-all": true,
      }
    }

    function createProxyGrep(name, addProxies) {
      addProxies = addProxies ? (Array.isArray(addProxies) ? addProxies : [addProxies]) : [];
      return {
        "name": name,
        "type": "select",
        "proxies": [...addProxies, "DIRECT", "普通节点"],
        "include-all": true,
      }
    }

    const regionGroups = [
      {
        "name": "普通节点",
        "type": "url-test",
        "url": "http://www.gstatic.com/generate_204",
        "interval": 300,
        "tolerance": 100,
        "include-all": true,
        "filter": "^(?!.*(?:套餐|剩余|过期|链式|http|：|分享)).*$",
      },
      {
        "name": "家宽节点",
        "type": "url-test",
        "url": "http://www.gstatic.com/generate_204",
        "interval": 300,
        "tolerance": 100,
        "include-all": true,
        "filter": "^(?!.*链式)(?=.*家宽|住宅).+$",
        "proxies": ["链式节点"]
      },
      {
        "name": "链式节点",
        "type": "url-test",
        "url": "http://www.gstatic.com/generate_204",
        "interval": 300,
        "tolerance": 100,
        "include-all": true,
        "filter": ".*(链式).*"
      }
    ];
    const proxyGfwGroups = [];
    const proxyGroups = [];
    const ruleProviders = {};
    const rules = [];
    for (const { name, gfw, urls, payload, extraProxies } of proxyGrepConfig) {
      if (gfw) {
        proxyGfwGroups.push(createGfwProxyGrep(name, extraProxies));
      } else {
        proxyGroups.push(createProxyGrep(name, extraProxies));
      }
      if (payload) {
        rules.push(...createPayloadRules(payload, name));
      } else {
        const urlList = urls ? (Array.isArray(urls) ? urls : [urls]) : [];
        for (const index in urlList) {
          const theUrl = urlList[index];
          const iName = `${name}-rule${index != 0 ? `-${index}` : ''}`;
          ruleProviders[iName] = createRuleProviderUrl(theUrl);
          rules.push(`RULE-SET,${iName},${name}`);
        }
      }
    }


    return {
      mode: "rule",
      "find-process-mode": "strict",
      "global-client-fingerprint": "chrome",
      "unified-delay": true,
      "tcp-concurrent": true,
      filter: "^((?!Remain|Expired|官网|群组|节点|订阅|年|月|如需|套餐|去除|剩余|距离|测试|发布|网址|Reset).)+$",
      "geox-url": {
        geoip: "https://ghgo.xyz/https://github.com/MetaCubeX/meta-rules-dat/releases/download/latest/geoip-lite.dat",
        geosite: "https://ghgo.xyz/https://github.com/MetaCubeX/meta-rules-dat/releases/download/latest/geosite.dat",
      },
      dns: config.dns,
      "proxies": proxies,
      "proxy-providers": proxyProviders,
      "proxy-groups": [
        ...regionGroups,
        {
          "name": "国外网站",
          "type": "select",
          "url": "https://twitter.com/favicon.ico",
          "proxies": ["普通节点", "DIRECT"],
          "include-all": true,
        },
        {
          "name": "被墙网站",
          "type": "select",
          "proxies": ["普通节点", "DIRECT"],
          "include-all": true
        },
        ...proxyGroups,
        {
          "name": "国内网站",
          "type": "select",
          "proxies": ["DIRECT", "普通节点"],
          "include-all": true,
          "url": "https://www.baidu.com/favicon.ico"
        },
        ...proxyGfwGroups
      ],
      "rule-providers": ruleProviders,
      rules: [
        ...rules,
        "GEOSITE,gfw,被墙网站",
        "GEOIP,CN,国内网站",
        "MATCH,国外网站"
      ]
    };
  }
