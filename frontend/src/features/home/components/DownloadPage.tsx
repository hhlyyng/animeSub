const DownloadPage = () => (
    <div className="page-content">
      <h1>下载页面</h1>
      <p>这里是下载页面内容...</p>
      <div className="content-card">
        <h2>可用下载</h2>
        <div className="download-item">
          <span>文件 1.pdf</span>
          <button className="download-btn">下载</button>
        </div>
        <div className="download-item">
          <span>文件 2.docx</span>
          <button className="download-btn">下载</button>
        </div>
      </div>
    </div>
  );

  export default DownloadPage;