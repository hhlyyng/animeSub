import { useEffect, useState } from "react"

const HomePage = ({username}:{username:string}) => {
    const [isExpanded, setIsExpanded] = useState(false);

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
    const toggleSideBar = () =>{
        setIsExpanded(!isExpanded);
    }

    return (
    //Main Container
    <div style={{
        display: "flex",
        height: "100vh",
        background: "#ffffff"
    }}>
        {/*Side bar div*/}
        <div>
            
        </div>

    </div>
    )
};

export default HomePage;