import React from "react"; 
import{ useState } from "react"

// Icon SVG
import  HomeIcon  from "../icon/HomeIcon"
import SettingIcon from "../icon/SettingIcon"
import DownloadIcon from "../icon/DownloadIcon"
import { DefaultSideBar, CollapseSideBarArrow, ExpandSideBarArrow } from "../icon/SidebarIcon"
import GithubIcon from "../icon/GithubIcon";
import LogoutIcon from "../icon/LogoutIcon";


const HomePage = ({username}:{username:string}) => {
    const [isExpanded, setIsExpanded] = useState(true);
    const [language, setLanguage] = useState("zh");  //'zh' or 'en'
    const [isToggleHoverd, setIsToggleHovered] = useState(false)
    const [showText, setShowText] = useState(true);
    //const [isLogin, setIsLogin] = useState(false)

    const text: {
        [key:string]:{
            home: string;
            settings: string;
            download: string;
        }
        } = {
            zh:{
                home:"主页",
                settings:"设置",
                download: "下载",
            },
            en:{
                home:"Homepage",
                settings: "Setting",  
                download: "Download",  
            }
        }
    

    //function: ToggleSidebar
    const toggleSideBar = () =>{
        if (isExpanded) {
            setShowText(false);
            setIsExpanded(false);
        } else {
            setIsExpanded(true);
            // set delay 120ms wait the side bar expended
            setTimeout(() => {
                setShowText(true);
            }, 120);
        }
    }

    //function Toggle Language
    const toggleLanguage = () => {
        setLanguage(language === "zh" ? "en" : "zh");
    }

    //
    const getToggleIcon = () => {
        if (!isToggleHoverd) {
            return <DefaultSideBar />;
        }
        return isExpanded? <CollapseSideBarArrow /> : <ExpandSideBarArrow />;
    }

      const currentLanguage = text[language];

      type NavButtonProps = {
        icon: React.ReactElement;
        text: string;
        isActive: boolean;
      };

      const navigationItems = [
        {icon: <HomeIcon/>, text: currentLanguage.home, isActive: true},
        {icon: <DownloadIcon />, text: currentLanguage.download, isActive: false},
        {icon: <SettingIcon />, text: currentLanguage.settings, isActive: false},
      ]

      const NavButton = ({ icon , text,  isActive}: NavButtonProps) => {
        return (
        <button style={{
            width: isExpanded ? "auto" : "44px",
            height: "44px",
            padding: isExpanded ? "8px 12px" : "8px",
            marginBottom: "4px",
            backgroundColor: isActive ? "#dfe1e5" : "transparent",
            color: "black",
            border: isActive? "1px solid black" : "none",
            borderRadius: "8px",
            cursor: "pointer",
            display: "flex",
            gap: isExpanded? "12px":"0px",
            alignItems: "center",
            justifyContent: isExpanded? "flex-start" : "center",
            textAlign: "left",
            fontSize: "15px",
            fontWeight: "600px",
            transition: "all 0.2s ease"
        }}
            onMouseEnter={(e) => {
                if (!isActive){
                    (e.target as HTMLButtonElement).style.backgroundColor = "rgba(209,225,229,0.3)";
                }
            }}
            onMouseLeave={(e) => {
                if (!isActive) {
                    (e.target as HTMLButtonElement).style.backgroundColor = "transparent";
                }
            }}>
                <span style={{
                    height: "24px",
                    width: "24px",
                    fontSize: "24px",
                    alignItems: "center",
                    display:"flex",
                    justifyContent:"center"
                }}>
                    {icon}
                </span>
                {showText && <span>{text}</span>}
        </button> );
      }

    return (
    //Main Container
    <div style={{
        display: "flex",
        height: "100vh",
        background: "#ffffff"
    }}>
        {/*Side Bar div*/}
        <div style={{
            width: isExpanded? "240px" : "60px",
            backgroundColor: "#f7f7f8",
            display: "flex",
            position: "relative",
            color: "black",
            flexDirection: "column",
            transition: "width 0.1s ease"
        }}>
            {/*Top of the SideBar:Toggle Button and Logo */}
            <div style={{
                padding: "12px 16px",
                display: "flex",
                alignItems: "center",
                justifyContent:"center",
                height: "60px",
                minHeight: "60px"
                }}>
                    {/*Toggle Button*/}
                    <button 
                        onClick={toggleSideBar}
                        style={{
                            padding: "0 4px",
                            width: isExpanded? "32px":"44px",
                            height: isExpanded? "32px":"44px",
                            borderRadius: "4px",
                            backgroundColor: "transparent",
                            cursor: "pointer",
                            display: "flex",
                            alignItems: "center",
                            justifyContent:"center",
                            fontSize: "16px",
                            transition: "all 0.3s ease",
                            flexShrink: 0,
                            border: "none",
                        }}
                        onMouseEnter={(e)=>{
                            setIsToggleHovered(true);
                            (e.target as HTMLButtonElement).style.backgroundColor = "transparent";
                        }}
                        onMouseLeave={(e)=>{
                            setIsToggleHovered(false);
                            (e.target as HTMLButtonElement).style.backgroundColor = "transparent";
                        }}
                        >
                            {/* 🎯 动态图标显示 */}
                            <span style={{
                                width: isExpanded? "100%": "24px",
                                height: isExpanded? "100%": "24px", 
                                display: "flex",
                                alignItems: "center",
                                justifyContent: "center"
                            }}>
                                {/* 🔧 SVG占满修改2: 给SVG添加样式让它占满整个span容器 */}
                                <div style={{
                                    width: "100%",
                                    height: "100%",
                                    display: "flex",
                                    alignItems: "center",
                                    justifyContent: "center",
                                    color: "black",
                                }}>
                                    {React.cloneElement(getToggleIcon(), {
                                        style: {
                                            width: "100%",
                                            height: "100%",
                                            maxWidth: "100%",
                                            maxHeight: "100%"
                                        }
                                    })}
                                </div>
                            </span>
                    </button>
                    {showText && (
                        <div style={{
                            display: "flex",
                            alignItems: "center",
                            justifyContent: "center",
                            flex: 1
                            }}>
                                <h2>AnimeSub</h2>
                        </div>
                    )}
            </div>
            {/*Navigation Buttons */}
            <div style={{
                padding: "0 4px",
                flex:1,
                display:"flex",
                flexDirection:"column",
                gap:"4px"
            }}>
                {navigationItems.map((item, index) => (
                    <NavButton
                        key={index}
                        icon={item.icon}
                        text={item.text}
                        isActive={item.isActive}
                    />
                ))}
          </div>

          <div style={{
            padding: "12px",
            display: "flex",
            alignItems: "center",
            justifyContent: isExpanded ? "flex-start" : "center",
          }}>
            <button
              onClick={() => {
                window.open("https://github.com/hhlyyng/anime-subscription.git", "_blank"); // 👈 请在引号内填写您的GitHub仓库地址
              }}
              style={{
                width: isExpanded ? "auto" : "44px",
                height: "44px",
                padding: isExpanded ? "8px 12px" : "8px",
                backgroundColor: "transparent",
                borderRadius: "8px",
                cursor: "pointer",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                gap: isExpanded ? "8px" : "0",
                transition: "all 0.2s ease",
                color: "#374151",
                border:"none",
              }}
              onMouseEnter={(e) => {
                const button = e.currentTarget as HTMLButtonElement ;
                button.style.border = "1px solid #9ca3af";
              }}
              onMouseLeave={(e) => {
                const button = e.currentTarget as HTMLButtonElement ;
                button.style.border = "none";
              }}
            >
              {/* 🔧 新增GitHub图标容器 */}
              <span style={{
                width: "24px",
                height: "24px",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                flexShrink: 0
              }}>
                <GithubIcon />
              </span>
              {/* 🔧 新增GitHub文字: 展开时显示GitHub文字 */}
              {showText && <span style={{ 
                fontSize: "14px", 
                fontWeight: "500" 
                }}>
                    {language === "zh" ? "项目地址" : "Github-Repo Address"}
                    </span>}
            </button>
          </div>

          {/*User Info div*/}
          <div style={{
            padding: "16px 12px",
            borderTop: "1px solid #e0e0e0",
            marginTop: "auto",
            display:"flex",
            flexDirection:"row",
          }}>
            {/* User Profile (TODO) */}
            <div style={{
                display:"flex",
                alignItems: "center",
                justifyContent: isExpanded? "flex-start" : "center",
                gap: isExpanded? "12px" : "0",
            }}>
                   <div style={{
                        width: "32px",
                        height: "32px",
                        backgroundColor: "#90A4AE",  // 背景色
                        borderRadius: "50%",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "center",
                        fontSize: "14px",
                        fontWeight: "bold",
                        color: "black",
                        flexShrink: 0
                    }}>
                        {username ? username.split(' ').map(n => n[0]).join('').toUpperCase() : 'U'}
                    </div>
                    {showText && (
                        <div style={{
                            display: "flex",
                            flexDirection: "row",
                            overflow: "hidden"
                        }}>
                            <span style={{
                                fontSize: "14px",
                                fontWeight: "bold",
                                color: "#333",
                                whiteSpace:"nowrap",
                                overflow:"hidden",
                                textOverflow:'ellipsis'
                            }}>
                                {username || "Guest User"}
                            </span>
                        </div>
                    )}
            </div>
          </div>
        </div>

    </div>
    )
};

export default HomePage;