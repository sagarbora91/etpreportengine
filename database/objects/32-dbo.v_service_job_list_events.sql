CREATE OR ALTER VIEW dbo.v_service_job_list_events AS
WITH state_dates AS (
  SELECT d.report_code,d.snapshot_date,
    DENSE_RANK() OVER(PARTITION BY d.report_code ORDER BY d.snapshot_date) reading_seq,
    COUNT(*) OVER(PARTITION BY d.report_code) reading_count
  FROM (SELECT DISTINCT report_code,snapshot_date FROM dbo.v_service_reading_windows WHERE report_code IN('S009','S010')) d
), presence AS (
  SELECT r.report_code,r.job_order_number,d.reading_seq,d.reading_count,d.snapshot_date,MAX(r.status_date) status_date
  FROM dbo.v_service_job_readings r JOIN state_dates d ON d.report_code=r.report_code AND d.snapshot_date=r.snapshot_date
  GROUP BY r.report_code,r.job_order_number,d.reading_seq,d.reading_count,d.snapshot_date
), sequenced AS (
  SELECT p.*,
    LAG(p.reading_seq) OVER(PARTITION BY p.report_code,p.job_order_number ORDER BY p.reading_seq) previous_seq,
    LAG(p.snapshot_date) OVER(PARTITION BY p.report_code,p.job_order_number ORDER BY p.reading_seq) previous_seen,
    LEAD(p.reading_seq) OVER(PARTITION BY p.report_code,p.job_order_number ORDER BY p.reading_seq) next_seq
  FROM presence p
), state_events AS (
  SELECT s.job_order_number,s.report_code,CONVERT(varchar(12),CASE WHEN s.previous_seq IS NULL THEN 'FirstSeen' ELSE 'Reappeared' END) event_kind,
    s.snapshot_date,s.previous_seen previous_snapshot_date,s.status_date
  FROM sequenced s WHERE s.previous_seq IS NULL OR s.previous_seq<s.reading_seq-1
  UNION ALL
  SELECT s.job_order_number,s.report_code,'LeftList',n.snapshot_date,s.snapshot_date,s.status_date
  FROM sequenced s JOIN state_dates n ON n.report_code=s.report_code AND n.reading_seq=s.reading_seq+1
  WHERE s.next_seq IS NULL OR s.next_seq>s.reading_seq+1
  UNION ALL
  SELECT s.job_order_number,s.report_code,'StillListed',s.snapshot_date,s.previous_seen,s.status_date
  FROM sequenced s WHERE s.reading_seq=s.reading_count AND s.previous_seq=s.reading_seq-1
), job_spans AS (
  SELECT r.job_order_number,r.report_code,MIN(r.snapshot_date) first_seen,MAX(r.snapshot_date) last_seen,
    MIN(r.first_status_date) first_status_date,MAX(r.status_date) last_status_date
  FROM dbo.v_service_job_readings r JOIN dbo.v_service_families f ON f.report_code=r.report_code AND f.read_rule='JobList'
  GROUP BY r.job_order_number,r.report_code
), list_dates AS (
  SELECT w.report_code,MAX(w.snapshot_date) latest_date
  FROM dbo.v_service_reading_windows w WHERE w.read_rule='JobList'
  GROUP BY w.report_code
), events AS (
  SELECT * FROM state_events
  UNION ALL
  SELECT job_order_number,report_code,'FirstSeen',first_seen,CONVERT(date,NULL),first_status_date FROM job_spans
  UNION ALL
  SELECT j.job_order_number,j.report_code,'StillListed',j.last_seen,j.first_seen,j.last_status_date
  FROM job_spans j JOIN list_dates d ON d.report_code=j.report_code
  WHERE j.last_seen=d.latest_date AND j.last_seen>j.first_seen
  UNION ALL
  SELECT j.job_order_number,j.report_code,'LeftList',n.next_date,j.last_seen,j.last_status_date
  FROM job_spans j CROSS APPLY (SELECT MIN(w.snapshot_date) next_date FROM dbo.v_service_reading_windows w
    WHERE w.report_code=j.report_code AND w.snapshot_date>j.last_seen) n
  WHERE n.next_date IS NOT NULL
)
SELECT e.job_order_number,e.report_code,f.list_label,e.event_kind,e.snapshot_date,e.previous_snapshot_date,e.status_date
FROM events e JOIN dbo.v_service_families f ON f.report_code=e.report_code
-- >>> GRANTS
DENY DELETE ON dbo.v_service_job_list_events TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_job_list_events TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_job_list_events TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_job_list_events TO etp_owner,etp_store_manager,etp_viewer;
